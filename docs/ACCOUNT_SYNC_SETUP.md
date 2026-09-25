# Feather Account & Sync Setup

Feather Account & Sync uses Supabase for authentication, MFA, row-level authorization, encrypted blob storage, and device coordination. Browser sync contents are encrypted on the Feather client before upload.

## Supabase project

Create a Supabase project and copy its project URL and publishable client key into:

`src/FeatherBrowser/Infrastructure/Supabase/FeatherSupabase.cs`

Only use a publishable client key in Feather. Never place a Supabase secret key or service-role key in the desktop application or repository.

## Database migrations

Run the migrations in this order from the Supabase SQL editor:

1. `supabase/migrations/001_feather_zero_knowledge_sync.sql`
2. `supabase/migrations/002_mfa_and_device_approval.sql`
3. `supabase/migrations/003_account_sync_completion.sql`

The migrations create the encrypted sync store, device registry, one-time device-transfer records, recovery-device registration, pending-device denial, and AAL2 checks.

## Authentication

Enable the Supabase Email provider. During local development, email confirmation can be disabled. For public releases, configure a production email-delivery path before relying on confirmation or password-reset email.

Feather uses authenticator-app TOTP for MFA. Sensitive sync operations require an AAL2 session.

## First-device flow

1. Create or sign into a Feather account.
2. Set up authenticator-app 2FA.
3. Verify the six-digit TOTP code.
4. Enable encrypted sync.
5. Feather generates the Sync Master Key and recovery secret locally.
6. Feather uploads only encrypted sync data and an encrypted key bundle.
7. Save the recovery key or create an encrypted `.feather-recovery` backup.
8. The current installation is registered as the first approved device.

## Second-device flow

1. Sign in on the second device.
2. Complete TOTP verification.
3. Request device approval.
4. Compare the pairing fingerprint on both Feather installations.
5. Approve the request from an existing authorized device.
6. The approving client encrypts the Sync Master Key specifically for the new device.
7. Supabase relays the encrypted one-time transfer.
8. The new client decrypts the key locally and begins normal sync.

The recovery key is not required when an already-approved device is available.

## Recovery flow

If no approved device is available, use either:

- the manually saved Feather recovery key, or
- an encrypted `.feather-recovery` backup file.

Recovery backup files are encrypted locally with AES-256-GCM. Their encryption key is derived from the user's backup password using PBKDF2-HMAC-SHA256 with 600,000 iterations and a random 256-bit salt.

The backup password is not sent to Supabase.

## Automatic sync

Automatic sync is disabled unless all of the following are true:

- the window is not private,
- the account is signed in,
- the session is AAL2,
- encrypted sync is enabled,
- automatic sync is enabled,
- the local device possesses the Sync Master Key.

The interval is configurable from 1 to 60 minutes.

## Synced data

The encrypted snapshot can include:

- bookmarks,
- quick links,
- browser settings,
- workspaces,
- Shield configuration,
- browsing history when explicitly enabled,
- normal-window session/open tabs when explicitly enabled.

Private-window state is never included.

Passwords, cookies, downloaded files, and favicons are not part of the current sync snapshot.

## Security boundaries

Supabase may see account metadata, device metadata, public device keys, ciphertext sizes, timestamps, and encrypted blobs. It does not receive the plaintext Sync Master Key, device private keys, plaintext recovery secret, or decrypted browser snapshot.

See [`account-security.md`](../account-security.md) for the full threat model and limitations.
