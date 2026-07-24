import { Component, computed, HostListener, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MarkdownComponent } from 'ngx-markdown';
import {
  ChatApiService,
  ChatMetrics,
  ImageContext,
  OllamaModelOption,
  TextFileContext
} from './chat-api.service';

type ChatRole = 'user' | 'assistant';
type ThemeMode = 'light' | 'dark';
type Provider = 'ollama' | 'gemini';

const ThemeStorageKey = 'chatbot-ui-theme-mode';
const MaxContextCharacters = 120_000;
const MaxContextImages = 4;

interface ImagePreview {
  fileName: string;
  mimeType: string;
  base64: string;
  previewUrl: string;
}

interface ChatMessage {
  role: ChatRole;
  content: string;
  metrics?: ChatMetrics;
  contextFileName?: string;
  contextImages?: ImagePreview[];
}

@Component({
  selector: 'app-root',
  imports: [
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatToolbarModule,
    MarkdownComponent
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  private readonly chatApi = inject(ChatApiService);

  readonly draft = signal('');
  readonly selectedProvider = signal<Provider>('gemini');
  readonly selectedOllamaModel = signal('');
  readonly ollamaModels = signal<OllamaModelOption[]>([]);
  readonly isSending = signal(false);
  readonly isSwitchingModel = signal(false);
  readonly modelSwitchStatus = signal('');
  readonly modelSwitchError = signal('');
  readonly contextFileName = signal('');
  readonly contextText = signal('');
  readonly contextImages = signal<ImagePreview[]>([]);
  readonly contextError = signal('');
  readonly dialogImages = signal<ImagePreview[]>([]);
  readonly dialogImageIndex = signal(0);
  readonly isDragOverComposer = signal(false);
  readonly messages = signal<ChatMessage[]>([]);
  readonly activeAssistantIndex = signal<number | null>(null);
  readonly themeMode = signal<ThemeMode>('light');
  readonly isDarkTheme = computed(() => this.themeMode() === 'dark');
  readonly isOllamaSelected = computed(() => this.selectedProvider() === 'ollama');
  readonly hasTextContext = computed(() => !!this.contextText());
  readonly hasImageContext = computed(() => this.contextImages().length > 0);
  readonly imageContextCount = computed(() => this.contextImages().length);
  readonly hasContext = computed(() => this.hasTextContext() || this.hasImageContext());
  readonly isImageDialogOpen = computed(() => this.dialogImages().length > 0);
  readonly activeDialogImage = computed(() => this.dialogImages()[this.dialogImageIndex()] ?? null);
  readonly isImageContextSupported = computed(() => {
    if (this.selectedProvider() === 'gemini') {
      return true;
    }

    const selectedModel = this.resolveSelectedOllamaModel();
    if (!selectedModel) {
      return false;
    }

    const option = this.ollamaModels().find(model => model.model === selectedModel);
    return option?.supportsImage ?? false;
  });
  readonly themeIcon = computed(() => (this.isDarkTheme() ? 'light_mode' : 'dark_mode'));
  readonly themeLabel = computed(() =>
    this.isDarkTheme() ? 'Switch to light mode' : 'Switch to dark mode'
  );
  readonly canSend = computed(
    () =>
      this.draft().trim().length > 0 &&
      !this.isSending() &&
      !this.isSwitchingModel() &&
      (!this.isOllamaSelected() || !!this.selectedOllamaModel())
  );

  constructor() {
    this.initializeTheme();
  }

  sendMessage(): void {
    void this.sendMessageInternal();
  }

  private async sendMessageInternal(): Promise<void> {
    const message = this.draft().trim();
    const context = this.resolveInputContext();
    const contextImages = this.contextImages();

    if (!message || this.isSending()) {
      return;
    }

    this.messages.update(messages => [
      ...messages,
      {
        role: 'user',
        content: message,
        contextFileName: context?.fileName,
        contextImages: [...contextImages]
      }
    ]);
    this.clearAllContext();
    this.draft.set('');
    this.isSending.set(true);
    const assistantIndex = this.appendAssistantMessage();
    this.activeAssistantIndex.set(assistantIndex);

    try {
      await this.chatApi.streamMessage(
        message,
        this.selectedProvider(),
        this.resolveCurrentModel(),
        {
          onChunk: chunk => {
            this.updateMessageAt(assistantIndex, current => ({
              ...current,
              content: current.content + chunk
            }));
          },
          onDone: (metrics, model) => {
            this.updateMessageAt(assistantIndex, current => ({
              ...current,
              metrics
            }));

            if (this.isOllamaSelected() && model) {
              this.selectedOllamaModel.set(model);
            }
          }
        },
        context
      );

      this.updateMessageAt(assistantIndex, current => ({
        ...current,
        content: current.content || 'The model returned an empty response.'
      }));
    } catch {
      this.updateMessageAt(assistantIndex, () => ({
        role: 'assistant',
        content: this.modelSwitchError() || 'The API is unavailable. Start the backend and try again.'
      }));
    } finally {
      this.isSending.set(false);
      this.activeAssistantIndex.set(null);
    }
  }

  private appendAssistantMessage(): number {
    let index = -1;

    this.messages.update(messages => {
      index = messages.length;
      return [...messages, { role: 'assistant', content: '' }];
    });

    return index;
  }

  private updateMessageAt(index: number, updater: (message: ChatMessage) => ChatMessage): void {
    this.messages.update(messages =>
      messages.map((message, currentIndex) =>
        currentIndex === index ? updater(message) : message
      )
    );
  }

  formatMetrics(metrics: ChatMetrics | undefined): string {
    if (!metrics) {
      return '';
    }

    const parts: string[] = [];

    if (typeof metrics.inputTokens === 'number') {
      parts.push(`Input: ${metrics.inputTokens} tok`);
    }

    if (typeof metrics.outputTokens === 'number') {
      parts.push(`Output: ${metrics.outputTokens} tok`);
    }

    if (typeof metrics.outputTokensPerSecond === 'number' && Number.isFinite(metrics.outputTokensPerSecond)) {
      parts.push(`Speed: ${metrics.outputTokensPerSecond.toFixed(1)} tok/s`);
    }

    return parts.join(' | ');
  }

  isPendingAssistant(index: number, message: ChatMessage): boolean {
    return (
      message.role === 'assistant' &&
      this.isSending() &&
      this.activeAssistantIndex() === index &&
      !message.content
    );
  }

  toggleTheme(): void {
    const nextMode: ThemeMode = this.isDarkTheme() ? 'light' : 'dark';
    this.themeMode.set(nextMode);
    this.persistTheme(nextMode);
  }

  setProvider(provider: string): void {
    void this.setProviderInternal(provider === 'gemini' ? 'gemini' : 'ollama');
  }

  setOllamaModel(model: string): void {
    void this.switchOllamaModel(model);
  }

  openContextFilePicker(fileInput: HTMLInputElement): void {
    if (this.isSending() || this.isSwitchingModel()) {
      return;
    }

    fileInput.value = '';
    fileInput.click();
  }

  openImageFilePicker(fileInput: HTMLInputElement): void {
    if (this.isSending() || this.isSwitchingModel() || !this.isImageContextSupported()) {
      return;
    }

    fileInput.value = '';
    fileInput.click();
  }

  async onContextFileSelected(event: Event): Promise<void> {
    this.contextError.set('');
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    try {
      const text = await file.text();
      const trimmed = text.trim();

      if (!trimmed) {
        this.contextError.set('Selected file is empty. Choose a file with text content.');
        this.clearContextFile();
        return;
      }

      if (trimmed.length > MaxContextCharacters) {
        this.contextError.set(
          `Selected file is too large (${trimmed.length} chars). Limit is ${MaxContextCharacters} chars.`
        );
        this.clearContextFile();
        return;
      }

      this.contextFileName.set(file.name);
      this.contextText.set(trimmed);
    } catch {
      this.contextError.set('Unable to read selected file. Try a plain text file.');
      this.clearContextFile();
    }
  }

  async onImageFileSelected(event: Event): Promise<void> {
    this.contextError.set('');
    const input = event.target as HTMLInputElement;
    if (!input.files || input.files.length === 0) {
      return;
    }

    await this.appendImageFiles(input.files);
  }

  onComposerDragOver(event: DragEvent): void {
    if (!this.isImageContextSupported()) {
      return;
    }

    event.preventDefault();
    this.isDragOverComposer.set(true);
  }

  onComposerDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.isDragOverComposer.set(false);
  }

  async onComposerDrop(event: DragEvent): Promise<void> {
    event.preventDefault();
    this.isDragOverComposer.set(false);

    if (!this.isImageContextSupported() || !event.dataTransfer?.files || event.dataTransfer.files.length === 0) {
      return;
    }

    await this.appendImageFiles(event.dataTransfer.files);
  }

  async onComposerPaste(event: ClipboardEvent): Promise<void> {
    if (!this.isImageContextSupported() || !event.clipboardData) {
      return;
    }

    const imageFiles = Array.from(event.clipboardData.items)
      .filter(item => item.kind === 'file' && item.type.startsWith('image/'))
      .map(item => item.getAsFile())
      .filter((file): file is File => !!file);

    if (imageFiles.length === 0) {
      return;
    }

    event.preventDefault();
    await this.appendImageFiles(imageFiles);
  }

  removeImageContextAt(index: number): void {
    if (index < 0) {
      return;
    }

    this.contextImages.update(images => images.filter((_, currentIndex) => currentIndex !== index));
  }

  clearImageContext(): void {
    this.contextImages.set([]);
  }

  private async appendImageFiles(files: Iterable<File>): Promise<void> {
    if (!this.isImageContextSupported()) {
      this.contextError.set('Selected provider/model does not support image context.');
      this.clearImageContext();
      return;
    }

    const selected = Array.from(files);
    const availableSlots = MaxContextImages - this.contextImages().length;

    if (availableSlots <= 0) {
      this.contextError.set(`You can attach up to ${MaxContextImages} images per message.`);
      return;
    }

    const candidates = selected.filter(file => file.type && file.type.startsWith('image/')).slice(0, availableSlots);

    if (candidates.length === 0) {
      this.contextError.set('Selected items do not contain supported images. Choose PNG, JPG, WEBP, or GIF files.');
      return;
    }

    const newImages: ImagePreview[] = [];

    for (const file of candidates) {
      try {
        const dataUrl = await this.readFileAsDataUrl(file);
        const match = /^data:([^;]+);base64,(.+)$/i.exec(dataUrl);
        if (!match) {
          continue;
        }

        newImages.push({
          fileName: file.name || 'uploaded-image',
          mimeType: match[1],
          base64: match[2],
          previewUrl: dataUrl
        });
      } catch {
        this.contextError.set('Unable to read one or more selected images. Try different files.');
      }
    }

    if (newImages.length === 0) {
      if (!this.contextError()) {
        this.contextError.set('Unable to read selected image. Try another image file.');
      }
      return;
    }

    this.contextImages.update(images => [...images, ...newImages]);

    if (selected.length > availableSlots) {
      this.contextError.set(`Only ${MaxContextImages} images are allowed per message.`);
      return;
    }

    this.contextError.set('');
  }

  clearContextFile(): void {
    this.contextFileName.set('');
    this.contextText.set('');
  }

  formatModelOption(option: OllamaModelOption): string {
    let modality = 'other';
    if (option.supportsText && option.supportsImage) {
      modality = 'text+image';
    } else if (option.supportsText) {
      modality = 'text';
    } else if (option.supportsImage) {
      modality = 'image';
    }

    const install = option.isInstalled ? 'installed' : 'not installed';
    return `${option.label} (${modality}, ${install})`;
  }

  openMessageImageDialog(images: ImagePreview[], startIndex: number): void {
    if (images.length === 0) {
      return;
    }

    this.dialogImages.set(images);
    this.dialogImageIndex.set(Math.max(0, Math.min(startIndex, images.length - 1)));
  }

  closeImageDialog(): void {
    this.dialogImages.set([]);
    this.dialogImageIndex.set(0);
  }

  setDialogImageIndex(index: number): void {
    if (!this.isImageDialogOpen()) {
      return;
    }

    const maxIndex = this.dialogImages().length - 1;
    this.dialogImageIndex.set(Math.max(0, Math.min(index, maxIndex)));
  }

  showPreviousDialogImage(): void {
    if (!this.isImageDialogOpen()) {
      return;
    }

    const images = this.dialogImages();
    const current = this.dialogImageIndex();
    const next = current <= 0 ? images.length - 1 : current - 1;
    this.dialogImageIndex.set(next);
  }

  showNextDialogImage(): void {
    if (!this.isImageDialogOpen()) {
      return;
    }

    const images = this.dialogImages();
    const current = this.dialogImageIndex();
    const next = current >= images.length - 1 ? 0 : current + 1;
    this.dialogImageIndex.set(next);
  }

  onImageDialogKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.closeImageDialog();
      return;
    }

    if (event.key === 'ArrowLeft') {
      this.showPreviousDialogImage();
      return;
    }

    if (event.key === 'ArrowRight') {
      this.showNextDialogImage();
    }
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscapeKeydown(_event: Event): void {
    if (this.isImageDialogOpen()) {
      this.closeImageDialog();
    }
  }

  private async setProviderInternal(nextProvider: Provider): Promise<void> {
    if (nextProvider === this.selectedProvider() || this.isSending() || this.isSwitchingModel()) {
      return;
    }

    if (!this.confirmSessionReset(`Switch provider to ${nextProvider}`)) {
      return;
    }

    this.selectedProvider.set(nextProvider);
    this.resetSession();

    if (nextProvider === 'ollama') {
      await this.prepareOllamaModelSelection(false);
      if (!this.isImageContextSupported()) {
        this.clearImageContext();
      }
    } else {
      this.modelSwitchError.set('');
      this.modelSwitchStatus.set('');
    }
  }

  private async switchOllamaModel(nextModel: string): Promise<void> {
    const trimmed = nextModel.trim();
    if (!trimmed || !this.isOllamaSelected() || this.isSending() || this.isSwitchingModel()) {
      return;
    }

    if (trimmed === this.selectedOllamaModel()) {
      return;
    }

    if (!this.confirmSessionReset(`Switch model to ${trimmed}`)) {
      return;
    }

    await this.warmupAndActivateModel(trimmed, true);
  }

  private async prepareOllamaModelSelection(resetSessionOnWarmup: boolean): Promise<void> {
    this.modelSwitchError.set('');

    if (this.ollamaModels().length === 0) {
      this.modelSwitchStatus.set('Loading local Ollama models...');
      try {
        const models = await this.chatApi.fetchOllamaModels();
        this.ollamaModels.set(models);
      } catch (error) {
        this.modelSwitchError.set(this.toErrorMessage(error));
        return;
      } finally {
        this.modelSwitchStatus.set('');
      }
    }

    const preferred = this.resolveDefaultOllamaModel();
    if (!preferred) {
      this.modelSwitchError.set('No Ollama models were returned by the backend. Pull a model and retry.');
      return;
    }

    await this.warmupAndActivateModel(preferred, resetSessionOnWarmup);
  }

  private async warmupAndActivateModel(model: string, resetSessionOnWarmup: boolean): Promise<void> {
    this.isSwitchingModel.set(true);
    this.modelSwitchError.set('');
    this.modelSwitchStatus.set(`Preparing model ${model}. This can take a few seconds...`);

    try {
      await this.chatApi.warmupOllamaModel(model);
      this.selectedOllamaModel.set(model);
      if (!this.isImageContextSupported()) {
        this.clearImageContext();
      }
      if (resetSessionOnWarmup) {
        this.resetSession();
      }
    } catch (error) {
      this.modelSwitchError.set(this.toErrorMessage(error));
    } finally {
      this.modelSwitchStatus.set('');
      this.isSwitchingModel.set(false);
    }
  }

  private resolveDefaultOllamaModel(): string {
    const models = this.ollamaModels();
    const installedRecommended = models.find(
      option => option.isRecommended && option.isInstalled && option.supportsText
    );
    if (installedRecommended) {
      return installedRecommended.model;
    }

    const anyInstalled = models.find(option => option.isInstalled && option.supportsText);
    if (anyInstalled) {
      return anyInstalled.model;
    }

    const recommended = models.find(option => option.isRecommended && option.supportsText);
    if (recommended) {
      return recommended.model;
    }

    return '';
  }

  private confirmSessionReset(actionLabel: string): boolean {
    if (this.messages().length === 0 || typeof window === 'undefined') {
      return true;
    }

    return window.confirm(`${actionLabel}? This starts a new chat session and clears current messages.`);
  }

  private resolveCurrentModel(): string | undefined {
    if (!this.isOllamaSelected()) {
      return undefined;
    }

    const selected = this.selectedOllamaModel().trim();
    return selected || undefined;
  }

  private resolveInputContext(): TextFileContext | undefined {
    const text = this.contextText().trim();
    const imageContexts = this.contextImages()
      .map<ImageContext>(image => ({
        base64: image.base64,
        mimeType: image.mimeType,
        fileName: image.fileName
      }))
      .slice(0, MaxContextImages);

    const hasText = !!text;
    const hasImage = imageContexts.length > 0 && this.isImageContextSupported();

    if (!hasText && !hasImage) {
      return undefined;
    }

    const context: TextFileContext = {};

    if (hasText) {
      context.text = text;
      context.fileName = this.contextFileName() || 'uploaded-context.txt';
    }

    if (hasImage) {
      context.images = imageContexts;
    }

    return context;
  }

  private resolveSelectedOllamaModel(): string {
    const selected = this.selectedOllamaModel().trim();
    return selected || this.resolveDefaultOllamaModel();
  }

  private clearAllContext(): void {
    this.clearContextFile();
    this.clearImageContext();
    this.contextError.set('');
  }

  private readFileAsDataUrl(file: File): Promise<string> {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => {
        if (typeof reader.result === 'string') {
          resolve(reader.result);
          return;
        }

        reject(new Error('Image read failed.'));
      };
      reader.onerror = () => reject(reader.error ?? new Error('Image read failed.'));
      reader.readAsDataURL(file);
    });
  }

  private resetSession(): void {
    this.messages.set([]);
    this.activeAssistantIndex.set(null);
    this.draft.set('');
  }

  private toErrorMessage(error: unknown): string {
    if (error instanceof Error && error.message) {
      return error.message;
    }

    return 'Operation failed. Please retry.';
  }

  private initializeTheme(): void {
    if (typeof window === 'undefined' || !window.localStorage) {
      return;
    }

    const storedMode = window.localStorage.getItem(ThemeStorageKey);
    if (storedMode === 'light' || storedMode === 'dark') {
      this.themeMode.set(storedMode);
      return;
    }

    const prefersDark = window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
    this.themeMode.set(prefersDark ? 'dark' : 'light');
  }

  private persistTheme(mode: ThemeMode): void {
    if (typeof window === 'undefined' || !window.localStorage) {
      return;
    }

    window.localStorage.setItem(ThemeStorageKey, mode);
  }
}
