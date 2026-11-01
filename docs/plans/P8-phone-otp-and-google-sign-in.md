# P8: phone OTP and Google sign-in

Status: **approved 2026-10-13. Built and tested (backend 694, customer app +22); migration `AddOtpAndSocialSignIn` applied to the dev database.** MSG91 sender is written but not run against a live account; Google needs your OAuth client ids (see testing notes A8).

## Decisions (all chosen by the owner)
- OTP and Google together; Apple later.
- Development log sender now; MSG91 sender written behind config. Outside Development an unconfigured sender stops the API from starting.
- **Verified-only matching.** OTP signs in only to an account whose phone was verified by OTP, otherwise it makes a new customer; password users verify their number from Profile. Google matches by Google id, then by Google-verified email; if that account's email was never verified, its password and open sessions are removed first (blocks someone who registered another person's email beforehand). Staff and admin emails can never be taken over through Google.

## Rules
- Number: Indian mobile only, stored as +91XXXXXXXXXX. Code: 6 digits, 5 minutes, 5 wrong tries, 30 seconds between requests, 5 requests per number per hour; plus per-IP limits on the routes.
- Only an HMAC of the code is stored (keyed with `Otp:CodeKey`, or the account reset-code key when empty), bound to purpose, number and, for verification, the account.
- Every wrong, expired, used or unknown-number case gives the same answer; the same answer is given for numbers with and without an account.
- Changing the number on a profile clears its verified mark.

## Database
`Users`: `PhoneVerifiedAt`, `EmailVerifiedAt`, `GoogleSubject` (unique when set); a verified number is unique (filtered index). New table `OtpChallenges`. Additive only.
