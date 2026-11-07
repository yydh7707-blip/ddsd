# Compivox website

This is a self-contained, browser-first Compivox website in `website/`. It is independent of the DEX desktop application and uses no package dependencies or build step.

## Run locally

From the repository root, run:

```powershell
python -m http.server 8000 --directory website
```

Open `http://localhost:8000`. NFC tag URLs can use `?nfc=<tag-id>` on static hosting. A clean `/nfc/<tag-id>` URL needs a hosting rewrite to `index.html`.

## Browser-only limitations

- The site does not include a server, tag database, or admin system. Tag registration, active status, creation dates, last access, and access counts are therefore explicitly unavailable; they are not fabricated.
- An NFC URL identifies the URL visit, not physical tag contact. Web NFC requires a secure context and a supporting browser, generally Android Chrome.
- Website response time and redirect visibility depend on the target's CORS and browser policy. The browser does not expose certificate issuer or expiry details to this page.
- Public URL checks reject common local/private address forms before requests, but browser-only validation cannot guarantee public DNS resolution. A production server-side URL checker would need DNS/IP validation, redirect revalidation, request limits, and network egress controls to defend against SSRF.
- Public DNS, address, and speed-check endpoints are third-party services. Their availability and CORS policy can affect results. No result is substituted when a check fails.
- Connection quality and streaming estimates only use measurements this page has actually collected.
- Public IP addresses are omitted from shared result text by default.
