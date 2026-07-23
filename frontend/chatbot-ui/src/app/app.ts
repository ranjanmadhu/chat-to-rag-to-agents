import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MarkdownComponent } from 'ngx-markdown';
import { ChatApiService, ChatMetrics } from './chat-api.service';

type ChatRole = 'user' | 'assistant';
type ThemeMode = 'light' | 'dark';

const ThemeStorageKey = 'chatbot-ui-theme-mode';

interface ChatMessage {
  role: ChatRole;
  content: string;
  metrics?: ChatMetrics;
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
  readonly selectedProvider = signal<'ollama' | 'gemini'>('gemini');
  readonly isSending = signal(false);
  readonly messages = signal<ChatMessage[]>([]);
  readonly activeAssistantIndex = signal<number | null>(null);
  readonly themeMode = signal<ThemeMode>('light');
  readonly isDarkTheme = computed(() => this.themeMode() === 'dark');
  readonly themeIcon = computed(() => (this.isDarkTheme() ? 'light_mode' : 'dark_mode'));
  readonly themeLabel = computed(() =>
    this.isDarkTheme() ? 'Switch to light mode' : 'Switch to dark mode'
  );
  readonly canSend = computed(() => this.draft().trim().length > 0 && !this.isSending());

  constructor() {
    this.initializeTheme();
  }

  sendMessage(): void {
    void this.sendMessageInternal();
  }

  private async sendMessageInternal(): Promise<void> {
    const message = this.draft().trim();

    if (!message || this.isSending()) {
      return;
    }

    this.messages.update(messages => [...messages, { role: 'user', content: message }]);
    this.draft.set('');
    this.isSending.set(true);
    const assistantIndex = this.appendAssistantMessage();
    this.activeAssistantIndex.set(assistantIndex);

    try {
      await this.chatApi.streamMessage(message, this.selectedProvider(), {
        onChunk: chunk => {
          this.updateMessageAt(assistantIndex, current => ({
            ...current,
            content: current.content + chunk
          }));
        },
        onDone: metrics => {
          this.updateMessageAt(assistantIndex, current => ({
            ...current,
            metrics
          }));
        }
      });

      this.updateMessageAt(assistantIndex, current => ({
        ...current,
        content: current.content || 'The model returned an empty response.'
      }));
    } catch {
      this.updateMessageAt(assistantIndex, () => ({
        role: 'assistant',
        content: 'The API is unavailable. Start the backend and try again.'
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
    this.selectedProvider.set(provider === 'gemini' ? 'gemini' : 'ollama');
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
