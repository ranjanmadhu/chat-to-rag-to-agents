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
  ChatToolOption,
  ImageContext,
  OllamaModelOption,
  TextFileContext,
  ToolObservability
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
  observability?: ToolObservability;
  enabledToolIds?: string[];
  contextFileName?: string;
  contextImages?: ImagePreview[];
}

interface ToolGroup {
  category: string;
  tools: ChatToolOption[];
}

interface ProviderCapabilities {
  supportsText: boolean;
  supportsImage: boolean;
  supportsTools: boolean;
  isInstalled: boolean;
  isCloud: boolean;
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
  readonly isLoadingTools = signal(false);
  readonly toolsError = signal('');
  readonly availableTools = signal<ChatToolOption[]>([]);
  readonly selectedToolIds = signal<string[]>([]);
  readonly isSwitchingModel = signal(false);
  readonly modelSwitchStatus = signal('');
  readonly modelSwitchError = signal('');
  readonly contextFileName = signal('');
  readonly contextText = signal('');
  readonly contextPdfFile = signal<File | null>(null);
  readonly contextPdfFileName = signal('');
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
  readonly hasPdfContext = computed(() => !!this.contextPdfFile());
  readonly hasImageContext = computed(() => this.contextImages().length > 0);
  readonly hasAvailableTools = computed(() => this.availableTools().length > 0);
  readonly selectedToolCount = computed(() => this.selectedToolIds().length);
  readonly groupedAvailableTools = computed<ToolGroup[]>(() => {
    const groups = new Map<string, ChatToolOption[]>();

    for (const tool of this.availableTools()) {
      const category = tool.category?.trim() || 'General';
      const bucket = groups.get(category);
      if (bucket) {
        bucket.push(tool);
      } else {
        groups.set(category, [tool]);
      }
    }

    return Array.from(groups.entries())
      .sort((a, b) => a[0].localeCompare(b[0]))
      .map(([category, tools]) => ({
        category,
        tools: [...tools].sort((a, b) => a.displayName.localeCompare(b.displayName))
      }));
  });
  readonly imageContextCount = computed(() => this.contextImages().length);
  readonly hasContext = computed(() => this.hasTextContext() || this.hasPdfContext() || this.hasImageContext());
  readonly isImageDialogOpen = computed(() => this.dialogImages().length > 0);
  readonly activeDialogImage = computed(() => this.dialogImages()[this.dialogImageIndex()] ?? null);
  readonly selectedOllamaModelOption = computed<OllamaModelOption | null>(() => {
    if (!this.isOllamaSelected()) {
      return null;
    }

    const selectedModel = this.resolveSelectedOllamaModel();
    if (!selectedModel) {
      return null;
    }

    return this.ollamaModels().find(model => model.model === selectedModel) ?? null;
  });
  readonly selectedProviderCapabilities = computed<ProviderCapabilities>(() => {
    if (this.selectedProvider() === 'gemini') {
      return {
        supportsText: true,
        supportsImage: true,
        supportsTools: true,
        isInstalled: true,
        isCloud: true
      };
    }

    const selectedModel = this.selectedOllamaModelOption();
    return {
      supportsText: selectedModel?.supportsText ?? false,
      supportsImage: selectedModel?.supportsImage ?? false,
      supportsTools: selectedModel?.supportsTools ?? false,
      isInstalled: selectedModel?.isInstalled ?? false,
      isCloud: false
    };
  });
  readonly isImageContextSupported = computed(() => {
    if (this.selectedProvider() === 'gemini') {
      return true;
    }

    return this.selectedOllamaModelOption()?.supportsImage ?? false;
  });
  readonly isToolSelectionSupported = computed(() => {
    if (this.selectedProvider() === 'gemini') {
      return true;
    }

    return this.selectedOllamaModelOption()?.supportsTools ?? false;
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
    void this.loadAvailableTools();
  }

  sendMessage(): void {
    void this.sendMessageInternal();
  }

  private async sendMessageInternal(): Promise<void> {
    const message = this.draft().trim();
    const pdfContextFile = this.contextPdfFile();
    const context = this.resolveInputContext();
    const contextImages = this.contextImages();
    const enabledToolIds = this.isToolSelectionSupported() ? this.selectedToolIds() : [];

    if (!message || this.isSending()) {
      return;
    }

    this.messages.update(messages => [
      ...messages,
      {
        role: 'user',
        content: message,
        enabledToolIds: [...enabledToolIds],
        contextFileName: context?.fileName ?? pdfContextFile?.name,
        contextImages: [...contextImages]
      }
    ]);
    this.clearAllContext();
    this.draft.set('');
    this.isSending.set(true);
    const assistantIndex = this.appendAssistantMessage();
    this.activeAssistantIndex.set(assistantIndex);

    try {
      const streamHandlers = {
        onChunk: (chunk: string) => {
          this.updateMessageAt(assistantIndex, current => ({
            ...current,
            content: current.content + chunk
          }));
        },
        onDone: (
          metrics?: ChatMetrics,
          model?: string,
          observability?: ToolObservability) => {
          this.updateMessageAt(assistantIndex, current => ({
            ...current,
            metrics,
            observability
          }));

          if (
            this.isOllamaSelected() &&
            model &&
            this.ollamaModels().some(option => option.model === model)
          ) {
            this.selectedOllamaModel.set(model);
          }
        }
      };

      if (pdfContextFile) {
        await this.chatApi.streamMessageWithPdf(
          message,
          this.selectedProvider(),
          this.resolveCurrentModel(),
          streamHandlers,
          pdfContextFile,
          context?.images,
          enabledToolIds
        );
      } else {
        await this.chatApi.streamMessage(
          message,
          this.selectedProvider(),
          this.resolveCurrentModel(),
          streamHandlers,
          context,
          enabledToolIds
        );
      }

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

  isToolSelected(toolId: string): boolean {
    return this.selectedToolIds().includes(toolId);
  }

  onToolSelectionChange(toolId: string, enabled: boolean): void {
    if (this.isSending() || this.isSwitchingModel() || !this.isToolSelectionSupported()) {
      return;
    }

    this.selectedToolIds.update(current => {
      if (enabled) {
        if (current.includes(toolId)) {
          return current;
        }

        return [...current, toolId];
      }

      return current.filter(id => id !== toolId);
    });
  }

  clearSelectedTools(): void {
    if (this.isSending() || this.isSwitchingModel()) {
      return;
    }

    this.selectedToolIds.set([]);
  }

  getToolSelectorLabel(): string {
    if (!this.isToolSelectionSupported()) {
      return 'Disabled for selected model';
    }

    const count = this.selectedToolCount();
    if (count === 0) {
      return 'Select tools';
    }

    if (count === 1) {
      return '1 tool selected';
    }

    return `${count} tools selected`;
  }

  getToolDisplayName(toolId: string | undefined): string {
    if (!toolId) {
      return '';
    }

    const tool = this.availableTools().find(option => option.id === toolId);
    return tool?.displayName ?? toolId;
  }

  getDecisionSourceLabel(source: ToolObservability['decisionSource']): string {
    if (!source) {
      return '';
    }

    if (source === 'ai') {
      return 'AI selected tool';
    }

    if (source === 'deterministic-fallback') {
      return 'Fallback matched tool';
    }

    return 'No tool executed';
  }

  hasDecisionDetails(message: ChatMessage): boolean {
    const observability = message.observability;
    return !!(
      observability?.decisionSource ||
      observability?.summary ||
      observability?.notUsedReason ||
      (observability?.steps && observability.steps.length > 0)
    );
  }

  hasAssistantDetails(message: ChatMessage): boolean {
    return !!(this.formatMetrics(message.metrics) || this.hasDecisionDetails(message));
  }

  getAssistantDetailsSummary(message: ChatMessage): string {
    const metrics = this.formatMetrics(message.metrics);
    if (metrics) {
      return `Token details (${metrics})`;
    }

    return 'Response details';
  }

  getUsedToolId(message: ChatMessage): string | undefined {
    return message.observability?.usedToolId;
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

  openPdfFilePicker(fileInput: HTMLInputElement): void {
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
      this.clearPdfContext();
    } catch {
      this.contextError.set('Unable to read selected file. Try a plain text file.');
      this.clearContextFile();
    }
  }

  onPdfFileSelected(event: Event): void {
    this.contextError.set('');
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    if (!this.isPdfFile(file)) {
      this.contextError.set('Only PDF files are supported for PDF context.');
      this.clearPdfContext();
      return;
    }

    this.contextPdfFile.set(file);
    this.contextPdfFileName.set(file.name);
    this.clearContextFile();
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

  clearPdfContext(): void {
    this.contextPdfFile.set(null);
    this.contextPdfFileName.set('');
  }

  formatModelOption(option: OllamaModelOption): string {
    const capabilities: string[] = [];

    if (option.supportsText && option.supportsImage) {
      capabilities.push('text+image');
    } else if (option.supportsText) {
      capabilities.push('text');
    } else if (option.supportsImage) {
      capabilities.push('image');
    } else {
      capabilities.push('other');
    }

    if (option.supportsTools) {
      capabilities.push('tools');
    }

    const install = option.isInstalled ? 'installed' : 'not installed';
    return `${option.label} (${capabilities.join('+')}, ${install})`;
  }

  formatModelCapabilitiesSubheader(option: OllamaModelOption): string {
    const tags: string[] = [];
    tags.push(option.supportsText ? 'text' : 'no-text');
    tags.push(option.supportsImage ? 'image' : 'no-image');
    tags.push(option.supportsTools ? 'tools' : 'no-tools');
    tags.push(option.isInstalled ? 'installed' : 'not installed');
    tags.push(`source:${this.getCapabilitySourceLabel(option.capabilitySource)}`);
    return tags.join(' • ');
  }

  getCapabilitySourceLabel(source: OllamaModelOption['capabilitySource']): string {
    if (source === 'runtime') {
      return 'runtime';
    }

    if (source === 'fallback') {
      return 'fallback';
    }

    return 'unknown';
  }

  getSelectedModelCapabilitySource(): string {
    const selected = this.selectedOllamaModelOption();
    if (!selected) {
      return '';
    }

    return this.getCapabilitySourceLabel(selected.capabilitySource);
  }

  getSelectedModelDisplayLabel(): string {
    const selected = this.selectedOllamaModelOption();
    if (selected) {
      return selected.label;
    }

    return this.selectedOllamaModel() || 'Select model';
  }

  isSelectedOllamaModel(modelName: string): boolean {
    return this.selectedOllamaModel() === modelName;
  }

  selectOllamaModelFromMenu(modelName: string, menu: HTMLDetailsElement): void {
    menu.open = false;
    void this.switchOllamaModel(modelName);
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
      this.enforceCapabilityConstraints();
    } else {
      this.modelSwitchError.set('');
      this.modelSwitchStatus.set('');
      this.enforceCapabilityConstraints();
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
      this.enforceCapabilityConstraints();
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
    this.clearPdfContext();
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

  private isPdfFile(file: File): boolean {
    if (file.type === 'application/pdf') {
      return true;
    }

    return file.name.toLowerCase().endsWith('.pdf');
  }

  private resetSession(): void {
    this.messages.set([]);
    this.activeAssistantIndex.set(null);
    this.draft.set('');
  }

  private async loadAvailableTools(): Promise<void> {
    this.isLoadingTools.set(true);
    this.toolsError.set('');

    try {
      const tools = await this.chatApi.fetchTools();
      this.availableTools.set(tools);
      this.selectedToolIds.update(current =>
        current.filter(id => tools.some(tool => tool.id === id))
      );
      this.enforceCapabilityConstraints();
    } catch (error) {
      this.availableTools.set([]);
      this.toolsError.set(this.toErrorMessage(error));
    } finally {
      this.isLoadingTools.set(false);
    }
  }

  private toErrorMessage(error: unknown): string {
    if (error instanceof Error && error.message) {
      return error.message;
    }

    return 'Operation failed. Please retry.';
  }

  private enforceCapabilityConstraints(): void {
    if (!this.isToolSelectionSupported() && this.selectedToolIds().length > 0) {
      this.selectedToolIds.set([]);
    }

    if (!this.isImageContextSupported() && this.contextImages().length > 0) {
      this.clearImageContext();
    }
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
