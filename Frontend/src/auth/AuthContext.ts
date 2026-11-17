import { createContext, useContext } from 'react'

export interface CurrentUser { id: string; email: string | null; roles: string[] }
export interface AuthContextValue {
  status: 'loading' | 'authenticated' | 'anonymous'
  user: CurrentUser | null
  error: string
  notice: string
  busy: boolean
  retry: () => void
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
}
export const AuthContext = createContext<AuthContextValue | null>(null)
export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('AuthProvider bulunamadı.')
  return context
}
export { roles, operationalRoles, homeForRoles } from './roleRouting'
