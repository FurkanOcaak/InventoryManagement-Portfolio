import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from './AuthContext'

export function AuthStatus() {
  const { error, retry } = useAuth()
  return <div className="auth-screen"><section className="auth-card" aria-live="polite"><h1>Oturum kontrolü</h1>{error ? <><p role="alert">{error}</p><button className="auth-primary" onClick={retry}>Tekrar Dene</button></> : <p role="status">Oturumunuz kontrol ediliyor...</p>}</section></div>
}

export function ProtectedRoute({ allowedRoles }: { allowedRoles?: string[] }) {
  const { status, user } = useAuth()
  if (status === 'loading') return <AuthStatus />
  if (status === 'anonymous') return <Navigate to="/login" replace />
  if (allowedRoles && !user?.roles.some((role) => allowedRoles.includes(role))) return <section className="auth-denied"><h1>Erişim engellendi</h1><p>Bu sayfayı görüntülemek için yetkiniz bulunmuyor.</p></section>
  return <Outlet />
}
