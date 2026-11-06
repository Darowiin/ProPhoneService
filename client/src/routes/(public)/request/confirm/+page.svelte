<script lang="ts">
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import Eyebrow from '#lib/components/Eyebrow.svelte';
	import Button from '#lib/components/Button.svelte';
	import { session } from '#lib/auth/session.svelte.ts';
	import { maskEmail } from '../../../../services/auth.ts';
	import { isMockMode } from '../../../../services/api.ts';

	const orderId = $derived(page.url.searchParams.get('id') ?? '');
	const email = $derived(page.url.searchParams.get('email') ?? '');

	let code = $state('');
	let busy = $state(false);
	let formError = $state('');
	let resendAfter = $state(60);
	let inputs: HTMLInputElement[] = $state([]);

	/** Svelte action: keeps the six code inputs addressable by index. */
	function updateRef(el: HTMLInputElement, index: number) {
		inputs[index] = el;
		return {
			destroy() {
				inputs[index] = undefined as unknown as HTMLInputElement;
			}
		};
	}

	let resendTimerId: ReturnType<typeof setInterval> | null = null;

	function startResendTimer(): void {
		resendAfter = 60;
		if (resendTimerId) clearInterval(resendTimerId);
		resendTimerId = setInterval(() => {
			resendAfter -= 1;
			if (resendAfter <= 0 && resendTimerId) {
				clearInterval(resendTimerId);
				resendTimerId = null;
			}
		}, 1000);
	}

	startResendTimer();

	function onCodeInput(event: Event, index: number): void {
		const input = event.currentTarget as HTMLInputElement;
		const clean = input.value.replace(/\D/g, '').slice(-1);
		input.value = clean;
		code = code.slice(0, index) + clean + code.slice(index + 1);
		if (clean && index < 5) inputs[index + 1]?.focus();
		if (/^\d{6}$/.test(code)) void confirm();
	}

	function onKeyDown(event: KeyboardEvent, index: number): void {
		if (event.key === 'Backspace' && !(event.currentTarget as HTMLInputElement).value) {
			code = code.slice(0, index) + code.slice(index + 1);
			inputs[index - 1]?.focus();
		}
	}

	function validate(): boolean {
		formError = '';
		if (!orderId) {
			goto('/request');
			return false;
		}
		if (!/^\d{6}$/.test(code)) {
			formError = 'Введите 6 цифр из письма.';
			return false;
		}
		return true;
	}

	async function confirm(): Promise<void> {
		if (busy || !validate()) return;
		busy = true;
		try {
			const authSession = await session.confirm(email.trim(), code);
			if (authSession.role === 'Master') {
				goto('/master/orders');
				return;
			}
			// Карточка 20: заявка активирована — ведём в ЛК, где видна история и статус.
			goto('/request/success?id=' + encodeURIComponent(orderId));
		} catch (error) {
			formError = error instanceof Error ? error.message : 'Неверный код. Попробуйте ещё раз.';
			code = '';
			inputs[0]?.focus();
		} finally {
			busy = false;
		}
	}

	async function resend(): Promise<void> {
		if (resendAfter > 0 || busy) return;
		if (isMockMode) {
			startResendTimer();
			return;
		}
		try {
			await session.requestCode(email.trim());
			startResendTimer();
		} catch (error) {
			formError = error instanceof Error ? error.message : 'Не удалось отправить код повторно.';
		}
	}
</script>

<section class="container section confirm">
	<div class="card">
		<Eyebrow>Заявка принята</Eyebrow>
		<h1 class="title">Подтвердите почту</h1>
		<p class="lead">
			Отправили одноразовый код на <strong>{maskEmail(email) || 'указанную почту'}</strong>.
			Введёте его — заявка активируется, и вы отследите её в личном кабинете.
		</p>

		<div class="code-group" aria-label="Код из 6 цифр">
			{#each ['c1', 'c2', 'c3', 'c4', 'c5', 'c6'] as c, i (c)}
				<input
					class="code-input"
					type="text"
					inputmode="numeric"
					maxlength="1"
					autocomplete="one-time-code"
					aria-label={"Цифра " + (i + 1)}
					oninput={(e) => onCodeInput(e, i)}
					onkeydown={(e) => onKeyDown(e, i)}
					use:updateRef={i}
				/>
			{/each}
		</div>

		{#if formError}
			<p class="form-error" role="alert">{formError}</p>
		{/if}

		<div class="submit">
			<Button type="button" size="lg" disabled={!/^\d{6}$/.test(code) || busy} onclick={() => confirm()}>
				{busy ? 'Проверяем…' : 'Подтвердить заявку'}
			</Button>
		</div>

		<div class="links">
			{#if isMockMode || resendAfter <= 0}
				<button class="link-action" type="button" onclick={resend}>Отправить код повторно</button>
			{:else}
				<span class="link-muted">Отправить код повторно через 00:{String(resendAfter).padStart(2, '0')}</span>
			{/if}
			<button class="link-action link-action--muted" type="button" onclick={() => goto('/request')}>
				← Вернуться к заявке
			</button>
		</div>
	</div>
</section>

<style>
	.confirm {
		display: flex;
		justify-content: center;
		padding-block: 40px 72px;
	}

	.card {
		max-width: 560px;
		width: 100%;
		background: var(--surface);
		border: 1px solid var(--border);
		border-radius: 24px;
		padding: 40px;
	}

	.title {
		font-size: 26px;
		font-weight: 700;
		letter-spacing: -0.02em;
	}

	.lead {
		color: var(--muted);
		font-size: 14px;
		line-height: 1.6;
		margin-top: 8px;
	}

	.lead strong {
		color: var(--text);
	}

	.code-group {
		display: grid;
		grid-template-columns: repeat(6, 44px);
		gap: 8px;
		margin-top: 26px;
		max-width: 320px;
	}

	.code-input {
		width: 44px;
		height: 52px;
		text-align: center;
		font-size: 20px;
		font-weight: 600;
		background: var(--bg);
		border: 1px solid var(--border);
		border-radius: 8px;
		transition: border-color 0.15s ease;
	}

	.code-input:focus {
		border-color: var(--accent);
		outline: none;
	}

	.form-error {
		margin-top: 14px;
		font-size: 14px;
		color: var(--danger);
		background: var(--danger-soft);
		border-radius: 10px;
		padding: 10px 14px;
	}

	.submit {
		margin-top: 22px;
	}

	.links {
		margin-top: 18px;
		display: flex;
		flex-direction: column;
		gap: 8px;
		font-size: 13px;
	}

	.link-muted {
		color: var(--muted);
	}

	.link-action {
		font-size: 13px;
		color: var(--accent);
		text-align: left;
	}

	.link-action--muted {
		color: var(--muted);
	}

	.link-action:hover,
	.link-action--muted:hover {
		text-decoration: underline;
		text-underline-offset: 3px;
	}
</style>
