import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideMarkdown } from 'ngx-markdown';
import { of } from 'rxjs';
import { App } from './app';
import { ChatApiService } from './chat-api.service';

describe('App', () => {
  const chatApi = {
    sendMessage: vi.fn(() =>
      of({ message: '**A model** predicts the next useful token.', model: 'test' })
    ),
    streamMessage: vi.fn(async (_message: string, _provider: string | undefined, _model: string | undefined, handlers) => {
      handlers.onChunk('**A model** predicts the next useful token.');
      handlers.onDone?.();
    }),
    fetchOllamaModels: vi.fn(async () => [
      {
        model: 'llama3.2:1b',
        label: 'Llama 3.2 1B',
        supportsText: true,
        supportsImage: false,
        isInstalled: true,
        isRecommended: true
      }
    ]),
    warmupOllamaModel: vi.fn(async () => {})
  };

  beforeEach(() => {
    chatApi.sendMessage.mockClear();
    chatApi.streamMessage.mockClear();
    chatApi.fetchOllamaModels.mockClear();
    chatApi.warmupOllamaModel.mockClear();
  });

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideMarkdown(), { provide: ChatApiService, useValue: chatApi }]
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('renders the chat composer', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.textContent).toContain('LLM Tutorial Chatbot');
    expect(compiled.querySelector('textarea[name="message"]')).toBeTruthy();
  });

  it('sends a message and renders the response', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    fixture.componentInstance.draft.set('What is an LLM?');
    fixture.detectChanges();

    const form = fixture.debugElement.query(By.css('form')).nativeElement as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve));
    fixture.detectChanges();

    expect(chatApi.streamMessage).toHaveBeenCalledWith(
      'What is an LLM?',
      'gemini',
      undefined,
      expect.any(Object)
    );
    expect(fixture.nativeElement.textContent).toContain('A model predicts the next useful token.');
    expect(fixture.nativeElement.querySelector('.message-markdown strong')?.textContent).toBe(
      'A model'
    );
  });
});
