# Inventory Management ERP Frontend

React, TypeScript, Vite, and React Router client for the Inventory Management ERP
API. Includes role-aware management pages and the customer catalog/cart/order
portal. Authentication uses same-origin Identity cookies and CSRF protection;
credentials and session tokens are not stored in localStorage.

Read the [root README](../README.md) for architecture, account provisioning
limitations, and database setup. No API credentials or default accounts are bundled.

```powershell
npm ci
npm run dev
```

Vite uses port 5173 and proxies `/api` to `http://localhost:5098`. A real API requires
separately configured local infrastructure and accounts. Pending orders do not
perform fulfillment, payment, or automatic stock deduction.

```powershell
npm run build
npm run lint
node --test tests/apiFetch.test.mjs tests/roleRouting.test.mjs tests/commerce.test.mjs
```

With Vite running on 5173, the two browser scripts provide 14 isolated Chrome/CDP
scenarios. All API traffic is fulfilled by fixtures; they do not mutate a real DB.
Use `CHROME_PATH` if the default Windows Chrome binary is unavailable.
