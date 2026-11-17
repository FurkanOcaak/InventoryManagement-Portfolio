import { apiFetch } from '../api/apiFetch.ts'

export interface Page<T> { items: T[]; page: number; pageSize: number; totalCount: number }
export interface CatalogProduct { id: string; name: string; barcode: string; description: string | null; unitPrice: number }
export interface CartItem { productId: string; name: string; barcode: string; quantity: number; unitPrice: number; subtotal: number; available: boolean }
export interface Cart { version: string; items: CartItem[]; total: number }
export interface OrderSummary { id: string; number: string; createdAtUtc: string; status: string; total: number }
export interface Order extends OrderSummary { items: Omit<CartItem, 'available'>[] }
export interface AdminOrderSummary { order: OrderSummary; customerCode: string; customerName: string }
export interface AdminOrder { order: Order; customerCode: string; customerName: string }

export const money = (value: number) => new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' }).format(value)
export const orderDate = (value: string) => new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
export const stockNotice = 'Sipariş stok düşürmez veya rezerve etmez. Güncel stok kontrolü, siparişin daha sonra karşılanacağını garanti etmez. Siparişiniz onay bekler.'

export async function commerceRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await apiFetch(path, init)
  if (!response.ok) {
    const problem: unknown = await response.json().catch(() => null)
    const message = problem && typeof problem === 'object' && 'title' in problem && typeof problem.title === 'string' ? problem.title : ''
    throw new CommerceRequestError(response.status, response.status === 404 ? 'Sipariş veya kayıt bulunamadı.' : message || `İşlem tamamlanamadı (HTTP ${response.status}).`)
  }
  return response.json() as Promise<T>
}

export class CommerceRequestError extends Error {
  readonly status: number
  constructor(status: number, message: string) { super(message); this.status = status }
}

export const jsonRequest = (method: string, value: unknown): RequestInit => ({ method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(value) })

export function quantityValue(text: string): number | null {
  if (!/^\d+$/.test(text)) return null
  const value = Number(text)
  return Number.isSafeInteger(value) && value >= 1 && value <= 10000 ? value : null
}

// This is a request deduplication key, never an authentication token or a cart store.
export interface CheckoutAttempt { idempotencyKey: string; cartVersion: string }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
export function pendingCheckout(storage: Pick<Storage, 'getItem'>, userId: string): CheckoutAttempt | null {
  const value = storage.getItem(`inventory.checkout.${userId}`)
  if (!value) return null
  const parsed: unknown = JSON.parse(value)
  if (!parsed || typeof parsed !== 'object' || !('idempotencyKey' in parsed) || !('cartVersion' in parsed)
    || typeof parsed.idempotencyKey !== 'string' || typeof parsed.cartVersion !== 'string'
    || !uuid.test(parsed.idempotencyKey) || !uuid.test(parsed.cartVersion)) throw new Error('Sipariş isteği güvenle hazırlanamadı.')
  return parsed as CheckoutAttempt
}
export function checkoutAttempt(storage: Pick<Storage, 'getItem' | 'setItem'>, userId: string, cartVersion: string, makeKey = () => crypto.randomUUID()): CheckoutAttempt {
  const existing = pendingCheckout(storage, userId)
  if (existing) return existing
  const attempt = { idempotencyKey: makeKey(), cartVersion }
  storage.setItem(`inventory.checkout.${userId}`, JSON.stringify(attempt))
  return attempt
}
export function finishCheckout(storage: Pick<Storage, 'removeItem'>, userId: string) { storage.removeItem(`inventory.checkout.${userId}`) }
