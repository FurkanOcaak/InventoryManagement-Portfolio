type ApiEvent = { type: 'unauthorized' | 'notice'; message: string }
const listeners = new Set<(event: ApiEvent) => void>()
let generation = 0
let csrf: Promise<string> | undefined

export function subscribeApi(listener: (event: ApiEvent) => void) {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

function publish(event: ApiEvent) { for (const listener of listeners) listener(event) }

export function resetApiSession() { generation++; csrf = undefined }

export class ApiError extends Error {
  readonly status: number
  constructor(status: number, message: string) { super(message); this.name = 'ApiError'; this.status = status }
}

async function csrfToken(): Promise<string> {
  if (!csrf) {
    const epoch = generation
    const pending = (async () => {
      const response = await fetch('/api/auth/csrf', { credentials: 'same-origin', cache: 'no-store' })
      if (!response.ok) throw new ApiError(response.status, 'Güvenlik doğrulaması alınamadı. Tekrar deneyin.')
      const data: unknown = await response.json().catch(() => { throw new Error('Sunucudan geçerli güvenlik doğrulaması alınamadı.') })
      if (!data || typeof data !== 'object' || !('requestToken' in data) || typeof data.requestToken !== 'string'
        || !data.requestToken || !('headerName' in data) || data.headerName !== 'X-CSRF-TOKEN') {
        throw new Error('Geçerli güvenlik doğrulaması alınamadı.')
      }
      if (epoch !== generation) throw new DOMException('Oturum değişti.', 'AbortError')
      return data.requestToken
    })()
    csrf = pending
    void pending.catch(() => { if (csrf === pending) csrf = undefined })
  }
  return csrf
}

// Shared token requests remain alive; each caller can stop waiting independently.
function cancellable<T>(promise: Promise<T>, signal?: AbortSignal | null): Promise<T> {
  if (!signal) return promise
  signal.throwIfAborted()
  return new Promise((resolve, reject) => {
    const abort = () => reject(signal.reason ?? new DOMException('İstek iptal edildi.', 'AbortError'))
    signal.addEventListener('abort', abort, { once: true })
    promise.then(resolve, reject).finally(() => signal.removeEventListener('abort', abort))
  })
}

// Return Response so existing field validation, conflict handling and 204 flows stay intact.
// Mutating requests are never retried, even after token expiry or network failure.
export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  if (!path.startsWith('/api/') || path.includes('\\')) throw new Error('Geçersiz API adresi.')
  init.signal?.throwIfAborted()
  const epoch = generation
  const method = (init.method ?? 'GET').toUpperCase()
  const headers = new Headers(init.headers)
  if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(method)) {
    headers.set('X-CSRF-TOKEN', await cancellable(csrfToken(), init.signal))
  }
  init.signal?.throwIfAborted()
  if (epoch !== generation) throw new DOMException('Oturum değişti.', 'AbortError')
  const response = await fetch(path, { ...init, method, headers, credentials: 'same-origin', cache: 'no-store' })
  init.signal?.throwIfAborted()
  if (epoch !== generation) throw new DOMException('Oturum değişti.', 'AbortError')
  let message = ''
  if (response.status === 401 && path !== '/api/auth/login' && path !== '/api/auth/me') {
    message = 'Oturumunuz bulunamadı veya sona erdi. Lütfen yeniden giriş yapın.'
    resetApiSession()
    publish({ type: 'unauthorized', message })
  } else if (response.status === 403) message = 'Bu işlem için yetkiniz bulunmuyor.'
  else if (response.status === 429) message = 'Çok fazla istek gönderildi. Lütfen bir süre bekleyip tekrar deneyin.'
  else if (response.status === 400) {
    const problem: unknown = await response.clone().json().catch(() => null)
    if (problem && typeof problem === 'object' && 'title' in problem && problem.title === 'İstek doğrulanamadı.') {
      csrf = undefined
      message = 'Güvenlik doğrulaması sona erdi. İşlem tekrarlanmadı; bilgileri kontrol edip yeniden deneyin.'
    }
  }
  if (message) {
    if (response.status !== 401) publish({ type: 'notice', message })
    throw new ApiError(response.status, message)
  }
  return response
}
