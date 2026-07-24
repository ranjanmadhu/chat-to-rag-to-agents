import { Component, computed, inject, signal } from '@angular/core';
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
  OllamaModelOption,
  TextFileContext
} from './chat-api.service';

type ChatRole = 'user' | 'assistant';
type ThemeMode = 'light' | 'dark';
type Provider = 'ollama' | 'gemini';

const ThemeStorageKey = 'chatbot-ui-theme-mode';
const MaxContextCharacters = 120_000;

interface ChatMessage {
  role: ChatRole;
  content: string;
  metrics?: ChatMetrics;
  contextFileName?: string;
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
  readonly contextError = signal('');
  readonly messages = signal<ChatMessage[]>([]);
  readonly activeAssistantIndex = signal<number | null>(null);
  readonly themeMode = signal<ThemeMode>('light');
  readonly isDarkTheme = computed(() => this.themeMode() === 'dark');
  readonly isOllamaSelected = computed(() => this.selectedProvider() === 'ollama');
  readonly hasContext = computed(() => !!this.contextText());
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
    const context = this.resolveTextContext();

    if (!message || this.isSending()) {
      return;
    }

    this.messages.update(messages => [
      ...messages,
      { role: 'user', content: message, contextFileName: context?.fileName }
    ]);
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

  private resolveTextContext(): TextFileContext | undefined {
    const text = this.contextText().trim();
    if (!text) {
      return undefined;
    }

    return {
      text,
      fileName: this.contextFileName() || 'uploaded-context.txt'
    };
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
