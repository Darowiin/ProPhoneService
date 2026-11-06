import { browser } from '$app/env';
import * as auth from '../../services/auth.ts';
import { isMockMode } from '../../services/api.ts';
import type { AuthSession } from '../../services/auth.ts';

let current = $state<AuthSession | null>(null);
let restored = $state(false);

const STORAGE_KEY = 'pps-auth';

export function isTokenExpired(session: AuthSession | null): boolean {
	if (!session) return true;
	return Date.parse(session.accessTokenExpiresAtUtc) <= Date.now() + 30_000;
}

export function bearerToken(): string | null {
	return current && current.accessToken && !isTokenExpired(current) ? current.accessToken : null;
}

/**
 * Only the identity lives in localStorage — the bearer access token stays in
 * memory, so an XSS-gathered storage dump cannot be replayed against the API.
 * A reload gets a fresh access token from the HttpOnly refresh cookie.
 */
function persist(user: AuthSession | null): void {
	if (!browser) return;
	if (user) {
		const stored: StoredSession = {
			email: user.email,
			role: user.role,
			clientId: user.clientId,
			accessTokenExpiresAtUtc: user.accessTokenExpiresAtUtc
		};
		localStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
	} else {
		localStorage.removeItem(STORAGE_KEY);
	}
}

interface StoredSession {
	email: string;
	role: AuthSession['role'];
	clientId: string | null;
	/** Expiry timestamp only — the bearer token itself is not persisted. */
	accessTokenExpiresAtUtc: string;
}

async function restoreOnce(): Promise<void> {
	if (restored) return;
	const raw = browser ? localStorage.getItem(STORAGE_KEY) : null;
	let stored: StoredSession | null = null;
	if (raw) {
		try {
			stored = JSON.parse(raw) as StoredSession;
		} catch {
			localStorage.removeItem(STORAGE_KEY);
		}
	}
	try {
		// `ready` must flip only AFTER the refresh exchange settles: guards use
		// it to decide the redirect and would otherwise see an empty user for
		// the whole await and bounce the reloading user to /login.
		if (!stored) return;
		auth.rememberEmail(stored.email);
		if (isMockMode) {
			// No refresh cookie exists without a backend: rebuild the session so
			// the mock flow can survive a reload. The token is a placeholder, so
			// re-creating it exposes nothing.
			current = {
				email: stored.email,
				role: stored.role,
				clientId: stored.clientId,
				accessToken: 'mock-access-token',
				accessTokenExpiresAtUtc: stored.accessTokenExpiresAtUtc
			};
		} else {
			const refreshed = await auth.refresh();
			if (refreshed) {
				current = refreshed;
				persist(refreshed);
				return;
			}
			// Without a usable identity the stored session cannot do anything.
			current = null;
			persist(null);
		}
	} finally {
		restored = true;
	}
}

export const session = {
	/** Signed-in user, or null. Reactive. */
	get user(): AuthSession | null {
		return current;
	},
	/** True after the initial restore attempt (guards wait for it). */
	get ready(): boolean {
		return restored;
	},
	get role(): AuthSession['role'] | null {
		return current?.role ?? null;
	},
	start(email: string, authSession: AuthSession): void {
		auth.rememberEmail(email);
		current = authSession;
		persist(authSession);
	},
	/** Requests a one-time code to the given email. */
	requestCode(email: string): Promise<void> {
		auth.rememberEmail(email);
		return auth.requestCode(email);
	},
	/** Exchanges the code for tokens and signs the user in. */
	async confirm(email: string, code: string): Promise<AuthSession> {
		const authSession = await auth.confirm(email, code);
		current = authSession;
		persist(authSession);
		return authSession;
	},
	async logout(): Promise<void> {
		// No dedicated logout endpoint in card 1: dropping the cookie happens
		// client-side (refresh will fail -> 401).
		current = null;
		persist(null);
	}
};

/** Call once from the root layout so a reload keeps the session. */
export function initSession(): void {
	if (!restored) void restoreOnce();
}

/** Clears the local session (e.g. on a failed refresh after 401). */
export function dropSession(): void {
	current = null;
	persist(null);
}

/** API layer: a fresh access token from the /auth/refresh exchange replaces
 * the expired one in the reactive session (memory only, then re-persisted). */
export function installRefreshed(authSession: AuthSession): void {
	current = authSession;
	persist(authSession);
}
