import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface ChatRequest {
  message: string;
  provider?: string;
  model?: string;
  contextText?: string;
  contextFileName?: string;
  contextImages?: ImageContext[];
  contextImageBase64?: string;
  contextImageMimeType?: string;
  contextImageFileName?: string;
}

export interface ImageContext {
  base64: string;
  mimeType: string;
  fileName?: string;
}

export interface TextFileContext {
  text?: string;
  fileName?: string;
  images?: ImageContext[];
}

export interface ChatResponse {
  message: string;
  model: string;
  metrics?: ChatMetrics;
}

export interface ChatMetrics {
  inputTokens?: number;
  outputTokens?: number;
  outputTokensPerSecond?: number;
}

interface StreamPayload {
  content?: string;
  error?: string;
  metrics?: ChatMetrics;
  model?: string;
}

interface StreamHandlers {
  onChunk: (chunk: string) => void;
  onDone?: (metrics?: ChatMetrics, model?: string) => void;
}

export interface OllamaModelOption {
  model: string;
  label: string;
  supportsText: boolean;
  supportsImage: boolean;
  isInstalled: boolean;
  isRecommended: boolean;
}

@Injectable({ providedIn: 'root' })
export class ChatApiService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = this.resolveApiBaseUrl();

  sendMessage(
    message: string,
    provider?: string,
    model?: string,
    context?: TextFileContext
  ): Observable<ChatResponse> {
    return this.http.post<ChatResponse>(`${this.apiBaseUrl}/chat`, {
      message,
      provider,
      model,
      contextText: context?.text,
      contextFileName: context?.fileName,
      contextImages: context?.images
    } satisfies ChatRequest);
  }

  async streamMessage(
    message: string,
    provider: string | undefined,
    model: string | undefined,
    handlers: StreamHandlers,
    context?: TextFileContext
  ): Promise<void> {
    const response = await fetch(`${this.apiBaseUrl}/chat/stream`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Accept: 'text/event-stream'
      },
      body: JSON.stringify({
        message,
        provider,
        model,
        contextText: context?.text,
        contextFileName: context?.fileName,
        contextImages: context?.images
      } satisfies ChatRequest)
    });

    if (!response.ok || !response.body) {
      if (response.headers.get('content-type')?.includes('application/json')) {
        const error = (await response.json()) as { error?: string };
        throw new Error(error.error ?? 'Streaming request failed.');
      }

      throw new Error('Streaming request failed.');
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    while (true) {
      const { done, value } = await reader.read();
      if (done) {
        break;
      }

      buffer += decoder.decode(value, { stream: true });
      buffer = buffer.replace(/\r\n/g, '\n');

      let delimiterIndex = buffer.indexOf('\n\n');
      while (delimiterIndex !== -1) {
        const rawEvent = buffer.slice(0, delimiterIndex);
        buffer = buffer.slice(delimiterIndex + 2);
        this.handleSseEvent(rawEvent, handlers);
        delimiterIndex = buffer.indexOf('\n\n');
      }
    }

    const remaining = buffer.trim();
    if (remaining) {
      this.handleSseEvent(remaining, handlers);
    }
  }

  private handleSseEvent(rawEvent: string, handlers: StreamHandlers): void {
    let eventName = 'message';
    const dataLines: string[] = [];

    for (const line of rawEvent.split('\n')) {
      if (line.startsWith('event:')) {
        eventName = line.slice('event:'.length).trim();
      } else if (line.startsWith('data:')) {
        dataLines.push(line.slice('data:'.length).trim());
      }
    }

    if (dataLines.length === 0) {
      return;
    }

    const payloadText = dataLines.join('\n');
    const payload = JSON.parse(payloadText) as StreamPayload;

    if (eventName === 'chunk' && payload.content) {
      handlers.onChunk(payload.content);
      return;
    }

    if (eventName === 'error') {
      throw new Error(payload.error ?? 'Streaming failed.');
    }

    if (eventName === 'done') {
      handlers.onDone?.(payload.metrics, payload.model);
    }
  }

  async fetchOllamaModels(): Promise<OllamaModelOption[]> {
    let response: Response;
    try {
      response = await fetch(`${this.apiBaseUrl}/chat/ollama/models`, {
        method: 'GET',
        headers: {
          Accept: 'application/json'
        }
      });
    } catch {
      throw new Error('Backend API is unreachable. Start the backend dev server and retry.');
    }

    if (!response.ok) {
      const body = (await response.json().catch(() => null)) as { error?: string } | null;

      if (response.status === 503) {
        throw new Error(body?.error ?? 'Ollama is unavailable. Ensure ollama serve is running.');
      }

      throw new Error(body?.error ?? `Unable to load Ollama models (HTTP ${response.status}).`);
    }

    return (await response.json()) as OllamaModelOption[];
  }

  async warmupOllamaModel(model: string): Promise<void> {
    const response = await fetch(`${this.apiBaseUrl}/chat/ollama/warmup`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Accept: 'application/json'
      },
      body: JSON.stringify({ model })
    });

    if (response.ok) {
      return;
    }

    const body = (await response.json().catch(() => null)) as { error?: string } | null;
    throw new Error(body?.error ?? `Unable to warm up model '${model}'.`);
  }

  private resolveApiBaseUrl(): string {
    const globalConfig = (globalThis as { __CHATBOT_API_BASE_URL__?: unknown }).__CHATBOT_API_BASE_URL__;
    if (typeof globalConfig === 'string') {
      const trimmed = globalConfig.trim().replace(/\/$/, '');
      const looksLikeTemplateToken = /^__[^\s]+__$/.test(trimmed);
      if (trimmed && !looksLikeTemplateToken) {
        return trimmed;
      }
    }

    return '/api';
  }
}
