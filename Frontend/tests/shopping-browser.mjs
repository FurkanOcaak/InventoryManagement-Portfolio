// Isolated Chrome fixture: every /api request is fulfilled or failed by CDP, never sent to the real API.
import { spawn } from 'node:child_process'
import { mkdtemp, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import assert from 'node:assert/strict'

const origin = 'http://localhost:5173'
const profile = await mkdtemp(path.join(os.tmpdir(), 'inventory-shopping-browser-'))
const chrome = spawn(process.env.CHROME_PATH ?? 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe', [
  '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
  '--remote-debugging-port=0', '--remote-debugging-address=127.0.0.1', `--user-data-dir=${profile}`, 'about:blank',
], { windowsHide: true, stdio: ['ignore', 'ignore', 'pipe'] })
let socket, session, nextId = 0
const pending = new Map()
const failures = []
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))
const timer = setTimeout(() => { socket?.close(); chrome.kill() }, 90000)
try {
  const endpoint = await Promise.race([new Promise((resolve, reject) => {
    let output = ''
    chrome.stderr.on('data', (chunk) => { output += chunk; const match = output.match(/DevTools listening on (ws:\/\/[^\s]+)/); if (match) resolve(match[1]) })
    chrome.on('error', reject); chrome.on('exit', () => reject(new Error('Isolated browser exited')))
  }), sleep(15000).then(() => { throw new Error('Browser startup timed out') })])
  socket = new WebSocket(endpoint)
  await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }) })
  function command(method, params = {}, useSession = true) {
    const id = ++nextId
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)) }, 10000)
      pending.set(id, { resolve: (value) => { clearTimeout(timeout); resolve(value) }, reject })
      socket.send(JSON.stringify({ id, method, params, ...(useSession && session ? { sessionId: session } : {}) }))
    })
  }
  const id = '00000000-0000-4000-8000-000000000001'
  let role = 'Customer', forbiddenCatalog = false, sequence = 2
  const product = { id, name: 'Fixture Kahve', barcode: '00036000291452', description: 'Browser-only fixture', unitPrice: 12.5 }
  let cart = { version: '00000000-0000-0000-0000-000000000000', items: [], total: 0 }
  let order = null
  const checkoutRequests = []
  let writes = 0
  const version = () => `00000000-0000-4000-8000-${String(++sequence).padStart(12, '0')}`
  const setCart = (quantity) => { cart = { version: version(), items: quantity ? [{ productId: id, name: product.name, barcode: product.barcode, quantity, unitPrice: 12.5, subtotal: quantity * 12.5, available: true }] : [], total: quantity * 12.5 } }
  async function intercept({ requestId, request }) {
    const url = new URL(request.url)
    let status = 200, data = []
    if (['POST', 'PATCH', 'DELETE'].includes(request.method)) {
      writes++
      assert.equal(Object.entries(request.headers).find(([name]) => name.toLowerCase() === 'x-csrf-token')?.[1], 'fixture-csrf')
    }
    if (url.pathname === '/api/auth/me') data = { id, email: 'fixture@example.test', roles: [role] }
    else if (url.pathname === '/api/auth/csrf') data = { requestToken: 'fixture-csrf', headerName: 'X-CSRF-TOKEN' }
    else if (url.pathname === '/api/customer-portal/products') {
      status = forbiddenCatalog ? 403 : 200; data = { items: [product], page: 1, pageSize: 12, totalCount: 1 }
    } else if (url.pathname === '/api/customer-portal/cart') data = cart
    else if (url.pathname === '/api/customer-portal/cart/items') {
      const body = JSON.parse(request.postData)
      assert.deepEqual(Object.keys(body).sort(), ['cartVersion', 'productId', 'quantity'])
      setCart((cart.items[0]?.quantity ?? 0) + body.quantity); data = cart
    } else if (url.pathname.startsWith('/api/customer-portal/cart/items/')) {
      setCart(request.method === 'DELETE' ? 0 : JSON.parse(request.postData).quantity); data = cart
    } else if (url.pathname === '/api/customer-portal/orders' && request.method === 'POST') {
      const body = JSON.parse(request.postData); checkoutRequests.push(body)
      if (!order) {
        order = { id, number: 'ORD-BROWSER-FIXTURE', createdAtUtc: '2026-11-17T10:00:00Z', status: 'Pending', total: cart.total, items: cart.items.map(({ available, ...line }) => line) }
        setCart(0)
        await command('Fetch.failRequest', { requestId, errorReason: 'Failed' }); return
      }
      assert.deepEqual(body, checkoutRequests[0]); data = order
    } else if (url.pathname === '/api/customer-portal/orders') data = { items: order ? [order] : [], page: 1, pageSize: 20, totalCount: order ? 1 : 0 }
    else if (url.pathname.startsWith('/api/customer-portal/orders/')) { status = order ? 200 : 404; data = order }
    else if (url.pathname === '/api/orders') data = { items: [{ order, customerCode: 'FIXTURE', customerName: 'Browser customer' }], page: 1, pageSize: 20, totalCount: 1 }
    else if (url.pathname.startsWith('/api/orders/')) data = { order, customerCode: 'FIXTURE', customerName: 'Browser customer' }
    else if (url.pathname === '/api/customer-portal/me') data = { name: 'Browser customer' }
    else { status = 404; data = {} }
    await command('Fetch.fulfillRequest', { requestId, responseCode: status,
      responseHeaders: [{ name: 'Content-Type', value: 'application/json' }], body: Buffer.from(JSON.stringify(data)).toString('base64') })
  }
  socket.addEventListener('message', (event) => {
    const message = JSON.parse(event.data)
    if (message.id) {
      const entry = pending.get(message.id); pending.delete(message.id)
      if (message.error) entry?.reject(new Error(message.error.message)); else entry?.resolve(message.result)
    } else if (message.method === 'Fetch.requestPaused') void intercept(message.params).catch((error) => {
      if (!/Invalid InterceptionId|Invalid interceptionId|No resource with given identifier/i.test(error.message)) failures.push(error.message)
    })
    else if (message.method === 'Runtime.exceptionThrown') failures.push('Uncaught browser exception')
  })
  const target = await command('Target.createTarget', { url: 'about:blank' }, false)
  session = (await command('Target.attachToTarget', { targetId: target.targetId, flatten: true }, false)).sessionId
  await command('Runtime.enable'); await command('Page.enable')
  await command('Fetch.enable', { patterns: [{ urlPattern: `${origin}/api/*`, requestStage: 'Request' }] })
  const evaluate = async (expression) => {
    const result = await command('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true })
    if (result.exceptionDetails) throw new Error('Browser evaluation failed')
    return result.result.value
  }
  async function waitFor(expression) {
    for (let i = 0; i < 120; i++) { if (await evaluate(`Boolean(${expression})`)) return; await sleep(60) }
    throw new Error(`Browser condition timed out: ${expression}`)
  }
  const includes = (text) => `document.body.innerText.includes(${JSON.stringify(text)})`
  const navigate = (route) => command('Page.navigate', { url: origin + route })
  async function click(text) {
    await waitFor(`[...document.querySelectorAll('button')].some(x=>x.textContent.trim()===${JSON.stringify(text)}&&!x.disabled)`)
    await evaluate(`[...document.querySelectorAll('button')].find(x=>x.textContent.trim()===${JSON.stringify(text)}&&!x.disabled).click()`)
  }
  async function fill(selector, value) {
    await evaluate(`(()=>{const x=document.querySelector(${JSON.stringify(selector)});Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(x,${JSON.stringify(value)});x.dispatchEvent(new Event('input',{bubbles:true}));})()`)
  }
  await navigate('/portal/products'); await waitFor(includes('Fixture Kahve'))
  await click('Sepete Ekle'); await waitFor(includes('sepete eklendi'))
  assert.equal(cart.items[0].quantity, 1)
  console.log('PASS catalog renders leading-zero barcode and adds a server-priced item')
  await navigate('/portal/cart'); await waitFor('!!document.querySelector("input[id^=cart-]")')
  await fill('input[id^=cart-]', '2'); await click('Güncelle')
  await waitFor('document.querySelector("input[id^=cart-]").value === "2" && !document.querySelector("input[id^=cart-]").disabled')
  assert.equal(cart.total, 25)
  await evaluate('document.querySelector("input[type=checkbox]").click()'); await click('Siparişi Onayla')
  await waitFor(includes('Önceki sipariş isteğinin sonucu bekleniyor'))
  assert.equal(checkoutRequests.length, 1)
  await navigate('/portal/cart'); await waitFor(includes('Önceki sipariş isteğinin sonucu bekleniyor'))
  await evaluate('document.querySelector("input[type=checkbox]").click()'); await click('Önceki Siparişin Sonucunu Kontrol Et')
  await waitFor(includes('ORD-BROWSER-FIXTURE'))
  assert.deepEqual(checkoutRequests[0], checkoutRequests[1])
  assert.equal(await evaluate('sessionStorage.length'), 0)
  console.log('PASS quantity edit, lost checkout response, refresh and same-key replay produce one order')
  await navigate('/portal/orders'); await waitFor(includes('ORD-BROWSER-FIXTURE'))
  await navigate('/portal/cart'); await waitFor(includes('Sepetiniz boş'))
  await command('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true })
  assert.equal(await evaluate('document.documentElement.scrollWidth <= innerWidth'), true)
  console.log('PASS order history, empty cart and mobile layout')
  forbiddenCatalog = true; await navigate('/portal/products'); await waitFor(includes('Bu işlem için yetkiniz bulunmuyor'))
  assert.equal(await evaluate('location.pathname'), '/portal/products')
  console.log('PASS catalog 403 shows a useful error while retaining the session')
  role = 'Admin'; await navigate('/orders'); await waitFor(includes('ORD-BROWSER-FIXTURE'))
  await navigate('/orders/' + id); await waitFor(includes('Browser customer'))
  console.log('PASS Admin order list and detail')
  for (role of ['WarehouseStaff', 'Viewer', 'Customer']) { await navigate('/orders'); await waitFor(includes('Erişim engellendi')) }
  assert.ok(writes >= 4); assert.deepEqual(failures, [])
  console.log('PASS staff/viewer/customer cannot enter administrative orders. Real API requests: 0.')
} finally {
  clearTimeout(timer); socket?.close(); chrome.kill(); await sleep(500)
  const resolved = path.resolve(profile)
  if (resolved.startsWith(path.resolve(os.tmpdir()) + path.sep + 'inventory-shopping-browser-'))
    await rm(resolved, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 }).catch(() => {})
}
