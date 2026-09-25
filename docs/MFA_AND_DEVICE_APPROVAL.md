# Feather MFA and new-device approval

## What this adds

Feather now has two independent security layers:

1. Supabase TOTP MFA proves that the person signing in controls the account's second factor.
2. Feather device approval transfers the Sync Master Key from an already-authorized Feather installation to a new installation without exposing that key to Supabase.

A six-digit TOTP code is never used as an encryption key.

## Sign-in on a normal authorized device

1. Sign in with the Feather/Supabase account.
2. Complete TOTP MFA.
3. Supabase returns an `aal2` session.
4. Feather unlocks the device-local Sync Master Key with Windows DPAPI.
5. The encrypted sync blob is downloaded and decrypted locally.

## Sign-in on a second device

1. Sign in with account credentials.
2. Enter the six-digit TOTP code.
3. Feather creates a fresh local P-256 ECDH device key pair.
4. Only the public key and device metadata are registered with Supabase.
5. Feather shows a pairing fingerprint such as:

   `91C4-06AF-720E-95B1`

6. An existing authorized Feather device displays the pending request and the same fingerprint.
7. The user compares the fingerprint and presses Approve.
8. The authorized device derives a shared ECDH secret with the new device's public key.
9. A transfer key is derived with HKDF-SHA256.
10. The Sync Master Key is encrypted using AES-256-GCM.
11. Supabase relays only the encrypted transfer envelope.
12. The second device derives the same transfer key using its private key and the approving device's public key.
13. The second device decrypts the master key locally.
14. Before trusting it, Feather decrypts the current sync blob and checks the pinned key-bundle fingerprint.
15. The master key is saved locally with Windows DPAPI.
16. The one-time transfer envelope is deleted.

The recovery key is not required during this flow.

## Why TOTP alone cannot decrypt the vault

TOTP is an authentication factor. Its six-digit codes have intentionally small entropy and rotate frequently.

If successful TOTP authentication alone caused Supabase to provide a usable Sync Master Key, Supabase would necessarily control a path to that key. That would violate Feather's zero-knowledge design.

Therefore the secure choices are:

- TOTP + existing-device approval
- TOTP + encrypted recovery USB/phone
- TOTP + random recovery key
- a future cryptographic passkey recovery mechanism

## AAL2 enforcement

Migration `002_mfa_and_device_approval.sql` adds a restrictive RLS policy requiring:

```sql
auth.jwt()->>'aal' = 'aal2'
```

for ciphertext reads.

The `security definer` write functions also explicitly check AAL2 because security-definer functions bypass normal RLS.

This means a stolen password/token at AAL1 cannot even fetch the encrypted Feather blob through the normal client API.

## Pairing fingerprint

The fingerprint is derived from the new device's public key and displayed on both devices.

The user should compare the complete code before approving.

This protects against a compromised relay attempting to substitute a different public key during device approval.

Do not reduce the displayed fingerprint to a six-digit number. The v2 implementation displays 64 bits of the public-key SHA-256 digest as four groups of four hexadecimal characters.

## What Supabase sees

Supabase may see:

- account identity metadata
- MFA factor metadata
- device names
- device UUIDs
- public ECDH keys
- pairing transfer metadata
- encrypted master-key transfer envelope
- encrypted browser data
- timestamps and ciphertext sizes

Supabase does not receive:

- Sync Master Key
- device private ECDH keys
- recovery key
- decrypted browser snapshot
- plaintext bookmarks/history/settings

## Important threat-model note

Device approval protects confidentiality even if the relay stores or alters data, because the relay never gets the ECDH private keys or Sync Master Key.

The relay can still deny service, delete records, or show fake pending-device metadata.

The user-visible pairing fingerprint is therefore a real security control, not decorative UI.
