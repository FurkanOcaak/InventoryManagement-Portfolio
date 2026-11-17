import { useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Eye, EyeOff, Layers3 } from 'lucide-react'
import { Navigate } from 'react-router-dom'
import { useAuth, homeForRoles } from '../auth/AuthContext'
import { AuthStatus } from '../auth/ProtectedRoute'

export function LoginPage() {
  const { status, user, login, busy, notice } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [visible, setVisible] = useState(false)
  const [error, setError] = useState('')
  const pending = useRef(false)
  if (status === 'loading') return <AuthStatus />
  if (status === 'authenticated') return <Navigate to={homeForRoles(user?.roles ?? [])} replace />

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending.current || busy) return
    if (!email.trim() || !password.trim()) { setError('E-posta ve parola zorunludur.'); return }
    pending.current = true; setError('')
    try { await login(email, password); setPassword('') }
    catch (failure) { setError(failure instanceof Error ? failure.message : 'Giriş tamamlanamadı.') }
    finally { pending.current = false }
  }

  return <div className="auth-screen"><section className="auth-card" aria-labelledby="login-title">
    <div className="auth-brand"><Layers3 size={28} aria-hidden="true" />InventoryManagement</div>
    <p className="eyebrow">ENVANTER & OPERASYON</p><h1 id="login-title">Çalışma alanınıza giriş yapın</h1><p className="auth-description">Envanterinizi yönetmek için hesabınızla oturum açın.</p>
    {notice && <p className="auth-notice" role="alert">{notice}</p>}
    {error && <p className="product-form-error" id="login-error" role="alert">{error}</p>}
    <form onSubmit={submit} aria-describedby={error ? 'login-error' : undefined} aria-busy={busy}>
      <fieldset disabled={busy}><legend className="product-form-sr-only">Giriş bilgileri</legend>
        <div className="product-form-field"><label htmlFor="login-email">E-posta *</label><input id="login-email" type="email" autoComplete="username" required maxLength={256} value={email} onChange={(event) => setEmail(event.target.value)} /></div>
        <div className="product-form-field"><label htmlFor="login-password">Parola *</label><div className="auth-password"><input id="login-password" type={visible ? 'text' : 'password'} autoComplete="current-password" required value={password} onChange={(event) => setPassword(event.target.value)} /><button type="button" aria-label={visible ? 'Parolayı gizle' : 'Parolayı göster'} aria-pressed={visible} onClick={() => setVisible((value) => !value)}>{visible ? <EyeOff size={19} /> : <Eye size={19} />}</button></div></div>
        <button type="submit" className="auth-primary">{busy ? 'Giriş yapılıyor...' : 'Giriş Yap'}</button>
      </fieldset>
    </form>
  </section></div>
}
