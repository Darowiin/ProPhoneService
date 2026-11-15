// Auth service — passwordless phone-code sign in via VK (sprint 3):
//   POST /api/auth/request-code {phone}        → 204
//   POST /api/auth/confirm {phone, code}       → 200 {accessToken, role, clientId, ...}
//   POST /api/auth/refresh                     → new access token (HttpOnly refresh cookie)
// Authorization: Bearer <accessToken>; refresh token rides an HttpOnly cookie
// that the browser attaches automatically on same-origin requests.
//
// In mock mode (or while endpoints answer 404/405) the flow works without a
// backend: request-code succeeds instantly and the code is shown in the UI.
// VK is the only channel; a guest binds it through messengerLink from the
// order creation response.

import { ApiError, isMockMode, request } from './api.ts';

export type AccountRole = 'Client' | 'Master';

export interface AuthSession {
	/** Телефон аккаунта (VK-привязка задаётся сервером по этому контакту). */
	phone: string;
	role: AccountRole;
	clientId: string | null;
	/** JWT access token; memory-only — it never reaches localStorage. */
	accessToken: string;
	/** ISO instant when the access token stops being valid. */
	accessTokenExpiresAtUtc: string;
}

const delay = (ms = 250) => {
	const { promise, resolve } = Promise.withResolvers<void>();
	setTimeout(resolve, ms);
	return promise;
};

export function isPhone(phone: string): boolean {
	return /^\+7\d{10}$/.test(phone.trim());
}

/** Маска для экрана кода: +7 (916) 234-56-78 печатается как есть. */
export function maskPhone(phone: string): string {
	return phone.trim();
}

/** Mask for the code screen: a**@mail.ru */
export function maskEmail(email: string): string {
	const [name, domain] = email.split('@');
	if (!domain) return email;
	const visible = name.slice(0, Math.min(2, name.length));
	return `${visible}${'*'.repeat(Math.max(1, name.length - visible.length))}@${domain}`;
}

// ─── Requests ───────────────────────────────────────────────────────────────

// The phone is not present in the refresh response; keep the last known one.
let currentPhone: string | null = null;
export function rememberPhone(phone: string): void {
	currentPhone = phone;
}

export async function requestCode(phone: string): Promise<void> {
	if (isMockMode) {
		await delay(300);
		return;
	}
	try {
		await request<void>('/auth/request-code', { method: 'POST', body: JSON.stringify({ phone }) });
	} catch (error) {
		if (error instanceof ApiError && (error.status === 404 || error.status === 405)) return;
		throw error;
	}
}

export async function confirm(phone: string, code: string): Promise<AuthSession> {
	if (isMockMode) {
		await delay(400);
		return {
			phone,
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
	}>('/auth/confirm', { method: 'POST', body: JSON.stringify({ phone, code }) });

	return {
		phone,
		role: res.role,
		clientId: res.clientId,
		accessToken: res.accessToken,
		accessTokenExpiresAtUtc: res.accessTokenExpiresAtUtc
	};
}

/**
 * Refreshes the access token using the HttpOnly refresh cookie.
 * `phone` (optional) ties the refreshed session to the persisted identity:
 * the refresh response carries no phone itself, so at a cold start the
 * caller passes the one from storage — never an empty string.
 */
export async function refresh(phone?: string): Promise<AuthSession | null> {
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
			phone: phone ?? currentPhone ?? '',
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
