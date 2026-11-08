import { HttpClient, HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/**
 * A `fetch` that sends through the app's HttpClient, so its interceptors apply: the signed-in user's access token,
 * the tenant, DPoP proofs and the refresh-and-retry on an expired token. Give it to the Oluso SDK
 * (`new OlusoClient({ ..., fetch: httpClientFetch(http) })`) to run journeys the app's backend started for the user.
 * Answers with an error status resolve like fetch's do, so the SDK reads the server's message.
 */
export function httpClientFetch(http: HttpClient): typeof fetch {
  return async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    const method = (init?.method ?? 'GET').toUpperCase();

    let headers = new HttpHeaders();
    new Headers(init?.headers).forEach((value, name) => {
      headers = headers.set(name, value);
    });

    try {
      const response = await firstValueFrom(
        http.request(method, url, {
          body: init?.body ?? null,
          headers,
          observe: 'response',
          responseType: 'text',
          withCredentials: init?.credentials === 'include',
        })
      );
      return toResponse(response);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status > 0) return toResponse(error);
      throw error;
    }
  };
}

function toResponse(response: HttpResponse<string> | HttpErrorResponse): Response {
  const headers = new Headers();
  response.headers.keys().forEach((name) => {
    const value = response.headers.get(name);
    if (value !== null) headers.set(name, value);
  });
  const body = response instanceof HttpErrorResponse
    ? typeof response.error === 'string' ? response.error : JSON.stringify(response.error ?? null)
    : response.body;
  const empty = response.status === 204 || response.status === 205 || response.status === 304;
  return new Response(empty ? null : body, { status: response.status, statusText: response.statusText, headers });
}
