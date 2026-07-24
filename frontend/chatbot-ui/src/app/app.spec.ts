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
    streamMessage: vi.fn(async (_message: string, _provider: string | undefined, _model: string | undefined, handlers, _context) => {
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
      expect.any(Object),
      undefined
    );
    expect(fixture.nativeElement.textContent).toContain('A model predicts the next useful token.');
    expect(fixture.nativeElement.querySelector('.message-markdown strong')?.textContent).toBe(
      'A model'
    );
  });

  it('clears text context after sending a message', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    fixture.componentInstance.contextFileName.set('notes.txt');
    fixture.componentInstance.contextText.set('Roadmap notes');
    fixture.componentInstance.draft.set('Summarize');
    fixture.detectChanges();

    const form = fixture.debugElement.query(By.css('form')).nativeElement as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve));
    fixture.detectChanges();

    expect(chatApi.streamMessage).toHaveBeenCalledWith(
      'Summarize',
      'gemini',
      undefined,
      expect.any(Object),
      expect.objectContaining({ text: 'Roadmap notes', fileName: 'notes.txt' })
    );
    expect(fixture.componentInstance.contextText()).toBe('');
    expect(fixture.componentInstance.contextFileName()).toBe('');
    expect(fixture.componentInstance.hasContext()).toBe(false);
  });

  it('shows image preview on user message when image context is sent', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    fixture.componentInstance.contextImages.set([
      {
        fileName: 'diagram.png',
        mimeType: 'image/png',
        base64: 'AQIDBA==',
        previewUrl: 'data:image/png;base64,AQIDBA=='
      }
    ]);
    fixture.componentInstance.draft.set('What do you see?');
    fixture.detectChanges();

    const form = fixture.debugElement.query(By.css('form')).nativeElement as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve));
    fixture.detectChanges();

    const previewImage = fixture.nativeElement.querySelector('.message-image-preview img') as HTMLImageElement | null;
    expect(previewImage).toBeTruthy();
    expect(previewImage?.getAttribute('src')).toContain('data:image/png;base64,AQIDBA==');

    expect(chatApi.streamMessage).toHaveBeenCalledWith(
      'What do you see?',
      'gemini',
      undefined,
      expect.any(Object),
      expect.objectContaining({
        images: [
          expect.objectContaining({
            base64: 'AQIDBA==',
            mimeType: 'image/png',
            fileName: 'diagram.png'
          })
        ]
      })
    );
  });

  it('opens and closes image dialog when preview is clicked', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    fixture.componentInstance.contextImages.set([
      {
        fileName: 'diagram-1.png',
        mimeType: 'image/png',
        base64: 'AQIDBA==',
        previewUrl: 'data:image/png;base64,AQIDBA=='
      },
      {
        fileName: 'diagram-2.png',
        mimeType: 'image/png',
        base64: 'BQYHCA==',
        previewUrl: 'data:image/png;base64,BQYHCA=='
      }
    ]);
    fixture.componentInstance.draft.set('What do you see?');
    fixture.detectChanges();

    const form = fixture.debugElement.query(By.css('form')).nativeElement as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve));
    fixture.detectChanges();

    const openButton = fixture.debugElement.query(By.css('.image-preview-button'));
    expect(openButton).toBeTruthy();
    openButton.nativeElement.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.image-dialog img')).toBeTruthy();

    const nextButton = fixture.debugElement.query(By.css('.image-dialog-nav:last-of-type'));
    nextButton.nativeElement.click();
    fixture.detectChanges();

    expect((fixture.nativeElement.querySelector('.image-dialog img') as HTMLImageElement)?.src).toContain('BQYHCA==');

    const closeButton = fixture.debugElement.query(By.css('.image-dialog-close'));
    closeButton.nativeElement.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.image-dialog')).toBeNull();
  });

  it('clears image context after sending a message', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    fixture.componentInstance.contextImages.set([
      {
        fileName: 'diagram.png',
        mimeType: 'image/png',
        base64: 'AQIDBA==',
        previewUrl: 'data:image/png;base64,AQIDBA=='
      }
    ]);
    fixture.componentInstance.draft.set('Analyze image');
    fixture.detectChanges();

    const form = fixture.debugElement.query(By.css('form')).nativeElement as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve));
    fixture.detectChanges();

    expect(fixture.componentInstance.contextImages().length).toBe(0);
  });
});
