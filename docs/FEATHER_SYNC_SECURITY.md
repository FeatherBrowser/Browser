# Feather Sync security model

## Goal

Feather Sync is designed so the sync service stores encrypted data but does not possess the keys required to decrypt browser contents.

Supabase provides:

- account authentication
- authorization
- encrypted blob storage
- synchronization availability

Supabase is **not** part of the confidentiality boundary for browser contents.

## Keys

On the first Feather device:

1. Feather generates a random 256-bit Sync Master Key with the operating system CSPRNG.
2. Feather generates a separate random 256-bit Recovery Key.
3. The Sync Master Key encrypts browser sync data using AES-256-GCM.
4. The Recovery Key is used only to derive a wrapping key with HKDF-SHA256.
5. The wrapping key encrypts the Sync Master Key with AES-256-GCM.
6. Supabase receives only:
   - the encrypted/wrapped master key bundle
   - the encrypted browser blob
   - revision, timestamp, format and cryptographic metadata
7. The Recovery Key is shown to the user and is never uploaded.
8. The local Sync Master Key is stored with Windows DPAPI using CurrentUser scope.

The normal Supabase account password/token cannot derive the Sync Master Key.

## New device

A new device:

1. signs in to the Supabase account
2. downloads the encrypted key bundle and encrypted data
3. asks the user for the Feather Recovery Key
4. unwraps the Sync Master Key locally
5. decrypts the browser data locally
6. stores the Sync Master Key locally with DPAPI

The Recovery Key never needs to be sent to Supabase.

## Account password reset

Resetting the Supabase password does **not** recover encrypted browser data.

A user who loses:

- every authorized Feather device, and
- the Feather Recovery Key

loses access to the encrypted sync data permanently.

This is intentional. If the service operator could recover it, the service operator would necessarily possess a recovery path to user data.

## What the Feather/Supabase operator can see

Even with database administrator or Supabase service-role access, the operator can see only metadata and ciphertext, including:

- Supabase account identity information such as email/provider
- user UUID
- encrypted blob size
- encrypted key bundle
- sync revision
- update timestamps
- infrastructure/network logs available to the hosting provider

The operator cannot decrypt bookmarks, settings, workspaces, tabs or history without a user's client-side secret.

## What the server can still do

Zero-knowledge encryption protects confidentiality; it does not make the server trusted for availability.

A malicious or compromised server can:

- delete ciphertext
- refuse requests
- return corrupt ciphertext
- return an older valid snapshot
- replace the encrypted key bundle

Feather mitigates this by:

- AES-GCM authentication
- binding user id and revision into authenticated additional data
- pinning the key-bundle fingerprint on each authorized device
- storing the highest-seen revision locally and rejecting lower revisions
- using optimistic revision commits

An entirely new device cannot independently prove that a server has not replayed an old, correctly authenticated snapshot. Solving that requires an additional transparency/log or trusted-device protocol.

## Client compromise

No encryption design can protect plaintext from malware running as the same user while Feather is unlocked.

The model also cannot protect users from a malicious Feather binary deliberately written to exfiltrate keys.

For the open-source project, strengthen this with:

- signed releases
- protected release keys
- reproducible builds
- public source for the sync client
- dependency pinning and review
- no remote code in the key-management path
- independent security review before password/cookie sync is added

## Passwords and cookies

Do not sync browser passwords or cookies in the first release.

Feather's current password vault uses Windows DPAPI, which is appropriate for local protection but is not a cross-device encrypted vault format.

Passwords should get a separately reviewed vault protocol later.

## Algorithms

Version 1 uses:

- random keys: OS cryptographic RNG
- data encryption: AES-256-GCM
- master-key wrapping: AES-256-GCM
- key separation: HKDF-SHA256
- local key protection: Windows DPAPI CurrentUser
- recovery secret: random 256-bit key

No encryption key is derived from the Supabase account password.

Using a random 256-bit recovery secret avoids offline password-guessing risk entirely. If a human-memorable sync passphrase is added later, use Argon2id rather than replacing the random recovery-key design.
