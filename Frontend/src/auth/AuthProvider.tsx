import { useCallback, useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { apiFetch, ApiError, resetApiSession, subscribeApi } from '../api/apiFetch'
import { AuthContext } from './AuthContext'
import type { CurrentUser } from './AuthContext'

async function currentUser(signal?: AbortSignal): Promise<CurrentUser | null> {
  const response = await apiFetch('/api/auth/me', { signal })
  if (response.status === 401) return null
  if (!response.ok) throw new Error('Oturum doğrulanamadı. Bağlantıyı kontrol edip tekrar deneyin.')
  const data: unknown = await response.json().catch(() => { throw new Error('Sunucudan geçerli oturum bilgisi alınamadı.') })
  if (!data || typeof data !== 'object' || !('id' in data) || typeof data.id !== 'string' || !data.id
    || !('email' in data) || (data.email !== null && typeof data.email !== 'string')
    || !('roles' in data) || !Array.isArray(data.roles) || !data.roles.every((role) => typeof role === 'string')) {
    throw new Error('Sunucudan geçerli oturum bilgisi alınamadı.')
  }
  return data as CurrentUser
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<'loading' | 'authenticated' | 'anonymous'>('loading')
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [attempt, setAttempt] = useState(0)
  const [busy, setBusy] = useState(false)
  const pending = useRef(false)
  const mounted = useRef(false)
  const revision = useRef(0)

  useEffect(() => {
    mounted.current = true
    const unsubscribe = subscribeApi((event) => {
      setNotice(event.message)
      if (event.type === 'unauthorized') {
        revision.current++
        setUser(null)
        setStatus('anonymous')
      }
    })
    return () => { mounted.current = false; unsubscribe() }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    const epoch = ++revision.current
    void currentUser(controller.signal).then((value) => {
      if (controller.signal.aborted || epoch !== revision.current) return
      setUser(value); setStatus(value ? 'authenticated' : 'anonymous')
    }).catch(() => {
      if (!controller.signal.aborted && epoch === revision.current) setError('Oturum kontrol edilemedi. Sunucu bağlantısını kontrol edip tekrar deneyin.')
    })
    return () => controller.abort()
  }, [attempt])

  const retry = useCallback(() => { setError(''); setStatus('loading'); setAttempt((value) => value + 1) }, [])

  async function login(email: string, password: string) {
    if (pending.current) return
    pending.current = true; setBusy(true); setNotice('')
    try {
      resetApiSession()
      const response = await apiFetch('/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: email.trim(), password }) })
      if (response.status === 401) throw new Error('Giriş başarısız. E-posta ve parolanızı kontrol edin; gerekirse bir süre bekleyin.')
      if (response.status !== 204) throw new Error('Giriş tamamlanamadı. Bilgilerinizi kontrol edip tekrar deneyin.')
      resetApiSession()
      const value = await currentUser()
      if (!value) throw new Error('Giriş oturumu doğrulanamadı. Lütfen tekrar deneyin.')
      if (mounted.current) { setUser(value); setStatus('authenticated'); setError(''); setNotice('') }
    } catch (failure) {
      if (failure instanceof ApiError || failure instanceof Error && failure.name !== 'TypeError') throw failure
      throw new Error('Sunucuya ulaşılamadı. Oturum açılmış olabilir; sayfayı yenileyerek oturumu kontrol edin.')
    } finally { pending.current = false; if (mounted.current) setBusy(false) }
  }

  async function logout() {
    if (pending.current) return
    pending.current = true; setBusy(true); setNotice('')
    try {
      const response = await apiFetch('/api/auth/logout', { method: 'POST' })
      if (response.status !== 204) throw new Error('Logout failed')
      resetApiSession(); revision.current++
      if (mounted.current) { setUser(null); setStatus('anonymous'); setNotice(''); setError('') }
    } catch (failure) {
      if (mounted.current) setNotice(failure instanceof ApiError ? failure.message : 'Çıkış doğrulanamadı. Sunucu oturumu açık olabilir; bağlantıyı kontrol edip tekrar deneyin.')
    } finally { pending.current = false; if (mounted.current) setBusy(false) }
  }

  return <AuthContext.Provider value={{ status, user, error, notice, busy, retry, login, logout }}>{children}</AuthContext.Provider>
}
