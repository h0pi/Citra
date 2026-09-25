import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NotificationService } from '@core/services/notification.service';

import { SKIP_ERROR_TOAST, errorInterceptor } from './error.interceptor';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let notify: { error: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    notify = { error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: NotificationService, useValue: notify },
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  function fail(status: number, body: object | null, context?: HttpContext): unknown {
    let received: unknown;
    http.get('/test', { context }).subscribe({ error: (e) => (received = e) });
    controller.expectOne('/test').flush(body, { status, statusText: 'Error' });
    return received;
  }

  it('shows the ProblemDetails title and detail for a 404', () => {
    fail(404, { title: 'Resource not found.', status: 404, detail: "Bot 'x' was not found." });
    expect(notify.error).toHaveBeenCalledWith('Resource not found.', "Bot 'x' was not found.");
  });

  it('shows the first field message for a validation error', () => {
    fail(400, {
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { Name: ["'Name' must not be empty."] },
    });
    expect(notify.error).toHaveBeenCalledWith(
      'One or more validation errors occurred.',
      "'Name' must not be empty.",
    );
  });

  it('shows a generic message with the trace id for a 500', () => {
    fail(500, { title: 'An unexpected error occurred.', status: 500, traceId: 'abc:1' });
    expect(notify.error).toHaveBeenCalledWith(
      'Something went wrong',
      expect.stringContaining('abc:1'),
    );
  });

  it('does not toast a 401 (handled by the auth interceptor)', () => {
    fail(401, null);
    expect(notify.error).not.toHaveBeenCalled();
  });

  it('does not toast when the request opts out', () => {
    fail(404, { title: 'Not found' }, new HttpContext().set(SKIP_ERROR_TOAST, true));
    expect(notify.error).not.toHaveBeenCalled();
  });

  it('re-throws the error so callers can react', () => {
    const error = fail(403, { title: 'Forbidden.' });
    expect(error).toBeTruthy();
  });

  it('handles "server unreachable" (status 0)', () => {
    let received: unknown;
    http.get('/test').subscribe({ error: (e) => (received = e) });
    controller.expectOne('/test').error(new ProgressEvent('error'));
    expect(received).toBeTruthy();
    expect(notify.error).toHaveBeenCalledWith('Cannot reach the server', expect.any(String));
  });
});
