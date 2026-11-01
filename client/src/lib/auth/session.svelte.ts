import { browser } from '$app/env';
import * as auth from '../../services/auth.ts';
import type { AuthSession } from '../../services/auth.ts';

let current = $state<AuthSession | null>(null);
let restored = $state(false);

const STORAGE_KEY = 'pps-auth';

export function isTokenExpired(session: AuthSession | null): boolean {
	if (!session) return true;
	return Date.parse(session.accessTokenExpiresAtUtc) <= Date.now() + 30_000;
}

export function bearerToken(): string | null {
	return current && !isTokenExpired(current) ? current.accessToken : null;
}

function persist(user: AuthSession | null): void {
	if (!browser) return;
	if (user) localStorage.setItem(STORAGE_KEY, JSON.stringify(user));
	else localStorage.removeItem(STORAGE_KEY);
}

async function restoreOnce(): Promise<void> {
	if (restored) return;
	restored = true;
	const raw = browser ? localStorage.getItem(STORAGE_KEY) : null;
	if (raw) {
		try {
			current = JSON.parse(raw) as AuthSession;
		} catch {
			localStorage.removeItem(STORAGE_KEY);
		}
	}
	if (!current) return;
	// The access token lives ~15 minutes; a stored session is useless without
	// a valid refresh cookie, so exchange it on every cold start. A transient
	// refresh failure must not log the user out while the token is still valid.
	const refreshed = await auth.refresh();
	if (refreshed) current = refreshed;
	else if (isTokenExpired(current)) {
		current = null;
		persist(null);
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
