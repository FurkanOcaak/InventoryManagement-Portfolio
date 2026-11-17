import { apiFetch } from '../../api/apiFetch'
import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'

type Field = 'code' | 'name' | 'location'
type FieldErrors = Partial<Record<Field, string>>

const fields: { key: Field; label: string; maxLength: number; required: boolean }[] = [
  { key: 'code', label: 'Depo Kodu', maxLength: 50, required: true },
  { key: 'name', label: 'Depo Adı', maxLength: 200, required: true },
  { key: 'location', label: 'Konum', maxLength: 300, required: false },
]

interface EditableWarehouse {
  id: string
  code: string
  name: string
  location: string | null
}

export function WarehouseEditForm({ warehouse, onSuccess, onCancel }: { warehouse: EditableWarehouse; onSuccess: () => void; onCancel: () => void }) {
  const [values, setValues] = useState<Record<Field, string>>({ code: warehouse.code, name: warehouse.name, location: warehouse.location ?? '' })
  const [errors, setErrors] = useState<FieldErrors>({})
  const [generalError, setGeneralError] = useState('')
  const [saving, setSaving] = useState(false)
  const formRef = useRef<HTMLFormElement>(null)
  const requestRef = useRef<AbortController | null>(null)
  const submittingRef = useRef(false)

  useEffect(() => {
    formRef.current?.scrollIntoView({ block: 'start' })
    formRef.current?.querySelector<HTMLInputElement>('[name="code"]')?.focus({ preventScroll: true })
    return () => requestRef.current?.abort()
  }, [])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submittingRef.current) return

    const trimmed = { code: values.code.trim(), name: values.name.trim(), location: values.location.trim() }
    const nextErrors: FieldErrors = {}
    for (const field of fields) {
      if (field.required && !trimmed[field.key]) {
        nextErrors[field.key] = `${field.label} zorunludur; yalnızca boşluk içeremez.`
      } else if (trimmed[field.key].length > field.maxLength) {
        nextErrors[field.key] = `${field.label} en fazla ${field.maxLength} karakter olabilir.`
      }
    }
    setErrors(nextErrors)
    setGeneralError('')
    const firstInvalid = fields.find((field) => nextErrors[field.key])
    if (firstInvalid) {
      const input = formRef.current?.elements.namedItem(firstInvalid.key)
      if (input instanceof HTMLElement) input.focus()
      return
    }

    submittingRef.current = true
    setSaving(true)
    const controller = new AbortController()
    requestRef.current = controller
    try {
      const response = await apiFetch(`/api/warehouses/${encodeURIComponent(warehouse.id)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        signal: controller.signal,
        body: JSON.stringify({ ...trimmed, location: trimmed.location || null }),
      })
      if (controller.signal.aborted) return
      if (response.status === 200) {
        onSuccess()
        return
      }
      if (response.status === 404) {
        setGeneralError('Bu depo artık bulunamıyor. Güncelleme yapılamadı. Formu kapatıp depo listesini yenileyin.')
      } else if (response.status === 409) {
        setErrors({ code: 'Bu depo kodu başka bir kayıtta kullanılıyor. Farklı bir kod girin.' })
      } else if (response.status === 400) {
        const problem: unknown = await response.json().catch(() => null)
        if (controller.signal.aborted) return
        const backendErrors: FieldErrors = {}
        const otherErrors: string[] = []
        if (problem && typeof problem === 'object' && 'errors' in problem && problem.errors && typeof problem.errors === 'object') {
          for (const [key, messages] of Object.entries(problem.errors)) {
            const text = typeof messages === 'string' ? messages : Array.isArray(messages)
              ? messages.filter((message): message is string => typeof message === 'string').join(' ')
              : ''
            if (!text) continue
            const field = fields.find((item) => item.key.toLowerCase() === key.split('.').pop()?.toLowerCase())
            if (field) backendErrors[field.key] = [backendErrors[field.key], text].filter(Boolean).join(' ')
            else otherErrors.push(text)
          }
        }
        setErrors(backendErrors)
        if (otherErrors.length || !Object.keys(backendErrors).length) {
          setGeneralError(otherErrors.join(' ') || 'Gönderilen depo bilgileri kabul edilmedi. Alanları kontrol edip tekrar deneyin.')
        }
      } else if (response.status >= 500) {
        setGeneralError(`Sunucuda bir hata oluştu (HTTP ${response.status}). Güncelleme durumunu depo listesinden kontrol ettikten sonra tekrar deneyin.`)
      } else {
        setGeneralError(`Depo güncellenemedi (HTTP ${response.status}). Lütfen tekrar deneyin.`)
      }
    } catch {
      if (!controller.signal.aborted) {
        setGeneralError('Sunucuya ulaşılamadı veya yanıt alınamadı. Bağlantınızı ve depo listesini kontrol ettikten sonra tekrar deneyin.')
      }
    } finally {
      if (!controller.signal.aborted) {
        submittingRef.current = false
        setSaving(false)
      }
    }
  }

  return (
    <form ref={formRef} id="warehouse-edit-form" className="product-create-form warehouse-create-form warehouse-edit-form" onSubmit={submit} noValidate aria-labelledby="warehouse-edit-title" aria-busy={saving}>
      <div className="product-form-heading"><h2 id="warehouse-edit-title">Depoyu Düzenle</h2><p>Yıldız (*) ile işaretli alanlar zorunludur. Konum bilgisi isteğe bağlıdır.</p></div>
      {generalError && <p className="product-form-error" role="alert">{generalError}</p>}
      <fieldset disabled={saving}>
        <legend className="product-form-sr-only">Depo bilgileri</legend>
        <div className="product-form-grid">
          {fields.map((field) => (
            <div className={`product-form-field ${field.key === 'location' ? 'warehouse-form-wide' : ''}`} key={field.key}>
              <label htmlFor={`warehouse-edit-${field.key}`}>{field.label}{field.required && <span aria-hidden="true"> *</span>}</label>
              <input id={`warehouse-edit-${field.key}`} name={field.key} type="text" required={field.required} maxLength={field.maxLength} value={values[field.key]} onChange={(event) => {
                const value = event.target.value
                setValues((previous) => ({ ...previous, [field.key]: value }))
              }} aria-invalid={!!errors[field.key]} aria-describedby={errors[field.key] ? `warehouse-edit-${field.key}-error` : undefined} />
              {errors[field.key] && <p id={`warehouse-edit-${field.key}-error`} className="product-field-error" role="alert">{errors[field.key]}</p>}
            </div>
          ))}
        </div>
        <div className="product-form-actions"><button type="button" className="product-cancel" onClick={onCancel}>İptal</button><button type="submit" className="warehouses-retry">{saving ? 'Güncelleniyor...' : 'Kaydet'}</button></div>
      </fieldset>
    </form>
  )
}
