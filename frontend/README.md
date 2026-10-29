# EventTicketing frontend

React 19 + Vite + TypeScript + React Router, plain CSS.

## Run locally

1. Start the API in Development on `http://127.0.0.1:5276`.
2. Copy `.env.example` to `.env` if you need a different API base.
3. Install and run:

```powershell
npm install
npm run dev
```

The dev server uses `http://localhost:5173`, which is the origin allowed by the API CORS policy.

## Checks

```powershell
npm run build
npm run lint
```

`VITE_API_BASE_URL` must include the version segment, for example
`http://127.0.0.1:5276/api/v1`.

## Routes

- Public: `/`, `/events/:id`, `/login`, `/signup`
- Attendee: `/wallet`, `/my-tickets`, `/checkout/:id`
- Organizer: `/organizer`, `/organizer/events/new`, `/organizer/events/:id`, `/organizer/events/:id/edit`, `/organizer/check-in`
- Admin: `/admin`, `/admin/events`, `/admin/organizers`

Cloudflare Pages uses `public/_redirects` for SPA fallback.
