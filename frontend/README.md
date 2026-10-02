# Claims web (Angular 21)

```bash
npm install
npm start        # http://127.0.0.1:4200, proxies /api to http://127.0.0.1:5080
npx ng build     # production build into dist/
```

Start the API first (see the root README).

- `src/app/core`: auth service, interceptor, role guard, API client and models
- `src/app/shared`: status badge, timeline, formatting helpers
- `src/app/features/claimant`: my claims, report an incident, claim detail
- `src/app/features/staff`: claims queue, claim detail with actions, team dashboard
