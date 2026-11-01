// Auth service — passwordless email-code sign in per sprint-2 card 1:
//   POST /api/auth/request-code {email}        → 204
//   POST /api/auth/confirm {email, code}       → 200 {accessToken, role, clientId, ...}
//   POST /api/auth/refresh                     → new access token (HttpOnly refresh cookie)
// Authorization: Bearer <accessToken>; refresh token rides an HttpOnly cookie
// that the browser attaches automatically on same-origin requests.
//
// In mock mode (or while endpoints answer 404/405) the flow works without a
// backend: request-code succeeds instantly and the code is shown in the UI.

import { ApiError, isMockMode, request } from './api.ts';

export type AccountRole = 'Client' | 'Master';

export interface AuthSession {
	email: string;
	role: AccountRole;
	clientId: string | null;
	/** JWT access token; lives in memory + localStorage for quick restore. */
	accessToken: string;
	/** ISO instant when the access token stops being valid. */
	accessTokenExpiresAtUtc: string;
}

const delay = (ms = 250) => {
	const { promise, resolve } = Promise.withResolvers<void>();
	setTimeout(resolve, ms);
	return promise;
};

export function isEmail(identity: string): boolean {
	return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(identity.trim());
}

/** Mask for the code screen: a**@mail.ru */
export function maskEmail(email: string): string {
	const [name, domain] = email.split('@');
	if (!domain) return email;
	const visible = name.slice(0, Math.min(2, name.length));
	return `${visible}${'*'.repeat(Math.max(1, name.length - visible.length))}@${domain}`;
}

// ─── Requests ───────────────────────────────────────────────────────────────

// The email is not present in the refresh response; keep the last known one.
let currentEmail: string | null = null;
export function rememberEmail(email: string): void {
	currentEmail = email;
}

export async function requestCode(email: string): Promise<void> {
	if (isMockMode) {
		await delay(300);
		return;
	}
	try {
		await request<void>('/auth/request-code', { method: 'POST', body: JSON.stringify({ email }) });
	} catch (error) {
		if (error instanceof ApiError && (error.status === 404 || error.status === 405)) return;
		throw error;
	}
}

export async function confirm(email: string, code: string): Promise<AuthSession> {
	if (isMockMode) {
		await delay(400);
		return {
			email,
			role: 'Client',
			clientId: 'a1f3c2d4-0000-4a01-9c11-000000000000',
			accessToken: 'mock-access-token',
			accessTokenExpiresAtUtc: new Date(Date.now() + 15 * 60_000).toISOString()
		};
	}
	const res = await request<{
		accountId: string;
		clientId: string;
		role: AccountRole;
		accessToken: string;
		accessTokenExpiresAtUtc: string;
	}>('/auth/confirm', { method: 'POST', body: JSON.stringify({ email, code }) });

	return {
		email,
		role: res.role,
		clientId: res.clientId,
		accessToken: res.accessToken,
		accessTokenExpiresAtUtc: res.accessTokenExpiresAtUtc
	};
}

/** Refreshes the access token using the HttpOnly refresh cookie. */
export async function refresh(): Promise<AuthSession | null> {
	if (isMockMode) return null;
	try {
		const res = await request<{
			accountId: string;
			clientId: string;
			role: AccountRole;
			accessToken: string;
			accessTokenExpiresAtUtc: string;
		}>('/auth/refresh', { method: 'POST' });
		return {
			email: currentEmail ?? '',
			role: res.role,
			clientId: res.clientId,
			accessToken: res.accessToken,
			accessTokenExpiresAtUtc: res.accessTokenExpiresAtUtc
		};
	} catch (error) {
		if (error instanceof ApiError) return null;
		throw error;
	}
}
