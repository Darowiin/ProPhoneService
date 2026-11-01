<script lang="ts">
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import Eyebrow from '#lib/components/Eyebrow.svelte';
	import Button from '#lib/components/Button.svelte';
	import { session } from '#lib/auth/session.svelte.ts';
	import { isEmail, maskEmail } from '../../../services/auth.ts';
	import { showToast } from '#lib/components/toast.svelte.ts';
	import { ApiError, isMockMode } from '../../../services/api.ts';

	// ?next=/account or /master/orders after a guard redirect.
	const next = $derived(page.url.searchParams.get('next') ?? null);

	let step = $state<'email' | 'code'>('email');
	let email = $state('');
	let code = $state('');
	let busy = $state(false);
	let formError = $state('');
	let resendAfter = $state(60);

	const canSubmit = $derived(/^\d{6}$/.test(code) && !busy);

	let inputs: HTMLInputElement[] = $state([]);

	/** Svelte action: keeps the six code inputs addressable. */
	function updateRef(el: HTMLInputElement, index: number) {
		inputs[index] = el;
		return { destroy() { inputs[index] = undefined as unknown as HTMLInputElement; } };
	}

	function onCodeInput(event: Event, index: number): void {
		const input = event.currentTarget as HTMLInputElement;
		const clean = input.value.replace(/\D/g, '').slice(-1);
		input.value = clean;
		code = code.slice(0, index) + clean + code.slice(index + 1);
		if (clean && index < 5) inputs[index + 1]?.focus();
	}

	function onKeyDown(event: KeyboardEvent, index: number): void {
		if (event.key === 'Backspace' && !(event.currentTarget as HTMLInputElement).value) {
			code = code.slice(0, index) + code.slice(index + 1);
			inputs[index - 1]?.focus();
		}
	}

	// Auto submit when six digits are entered.
	$effect(() => {
		if (step === 'code' && /^\d{6}$/.test(code)) void submitCode();
	});

	function validateEmail(): boolean {
		formError = '';
		if (!isEmail(email)) {
			formError = 'Укажите действующую почту.';
			return false;
		}
		return true;
	}

	async function sendCode(): Promise<void> {
		if (busy || !validateEmail()) return;
		busy = true;
		try {
			await session.requestCode(email.trim());
			step = 'code';
			formError = '';
			code = '';
			startResendTimer();
			if (isMockMode) showToast('Демо-режим: подойдёт любой 6-значный код');
		} catch (error) {
			formError = error instanceof ApiError ? error.message : 'Не удалось отправить код. Попробуйте ещё раз.';
		} finally {
			busy = false;
		}
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

	async function resendCode(): Promise<void> {
		if (resendAfter > 0 || busy) return;
		await sendCode();
	}

	async function submitCode(): Promise<void> {
		if (!canSubmit || busy) return;
		busy = true;
		formError = '';
		try {
			const authSession = await session.confirm(email.trim(), code);
			if (authSession.role === 'Master') {
				showToast('Вход мастера выполнен');
				goto(next ?? '/master/orders');
			} else {
				goto(next ?? '/account');
			}
		} catch (error) {
			formError = error instanceof ApiError ? error.message : 'Неверный код. Попробуйте ещё раз.';
			code = '';
			inputs[0]?.focus();
		} finally {
			busy = false;
		}
	}

	function backToEmail(): void {
		step = 'email';
		formError = '';
		code = '';
	}
</script>

<section class="container section auth">
	<div class="auth__grid">
		<div class="card">
			<Eyebrow>Личный кабинет</Eyebrow>

			{#if step === 'email'}
				<h1 class="title">Рады вас видеть снова</h1>
				<p class="lead">Войдите, чтобы следить за ремонтом и видеть историю обращений.</p>

				<h2 class="form-title">Вход в личный кабинет</h2>
				<p class="form-note">Пароль не нужен — отправим вам код.</p>

				<div class="field">
					<label class="label" for="email">Почта</label>
					<input
						id="email"
						class="input"
						class:input--error={formError.includes('почту')}
						type="email"
						placeholder="anna@example.com"
						bind:value={email}
						onkeydown={(e) => e.key === 'Enter' && sendCode()}
					/>
				</div>

				{#if formError}
					<p class="form-error" role="alert">{formError}</p>
				{/if}

				<div class="submit">
					<Button type="button" size="lg" disabled={busy} onclick={sendCode}>
						{busy ? 'Отправляем…' : 'Получить код'}
					</Button>
				</div>

				<p class="hint">
					{#if isMockMode}
						Демо-режим: код вернём прямо в уведомлении — бэкенд не поднят.
					{:else}
						Пришлём одноразовый код на эту почту. Регистрация отдельной не нужна.
					{/if}
				</p>
			{:else}
				<h1 class="title">Осталось ввести код</h1>
				<p class="lead">Подтвердите почту, чтобы продолжить в личный кабинет.</p>

				<h2 class="form-title">Подтверждение почты</h2>
				<p class="form-note">
					Отправили код на <strong>{maskEmail(email)}</strong>
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
							onpaste={(e) => {
								const pasted = e.clipboardData?.getData('text').replace(/\D/g, '').slice(0, 6) ?? '';
								if (pasted) {
									e.preventDefault();
									code = pasted;
									for (const [j, input] of inputs.entries()) input.value = pasted[j] ?? '';
								}
							}}
							use:updateRef={i}
						/>
					{/each}
				</div>

				{#if formError}
					<p class="form-error" role="alert">{formError}</p>
				{/if}

				<div class="submit">
					<Button type="button" size="lg" disabled={!canSubmit} onclick={() => submitCode()}>
						{busy ? 'Проверяем…' : 'Подтвердить'}
					</Button>
				</div>

				<div class="code-links">
					{#if resendAfter > 0}
						<span class="link-muted">Отправить код повторно через 00:{String(Math.max(0, resendAfter)).padStart(2, '0')}</span>
					{:else}
						<button class="action" type="button" onclick={resendCode}>Отправить код повторно</button>
					{/if}
					<button class="action" type="button" onclick={backToEmail}>← Вернуться к вводу почты</button>
				</div>
			{/if}
		</div>

		<div class="benefits">
			<h2 class="benefits__title">Всё о ремонте — в одном месте</h2>
			<article class="benefit">
				<span class="benefit__icon" aria-hidden="true">
					<svg width="20" height="20" viewBox="0 0 24 24" fill="none">
						<path d="M12 3 5 5.5V12c0 4.4 3 7.4 7 9 4-1.6 7-4.6 7-9V5.5L12 3Z" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round" />
						<path d="m9 12 2.2 2.2L15.5 9.7" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" />
					</svg>
				</span>
				<div>
					<p class="benefit__name">Статус ремонта</p>
					<p class="benefit__text">Узнавайте, на каком этапе ваше устройство и когда его можно забрать.</p>
				</div>
			</article>
			<article class="benefit">
				<span class="benefit__icon" aria-hidden="true">
					<svg width="20" height="20" viewBox="0 0 24 24" fill="none">
						<path d="M5 4h11a2 2 0 0 1 2 2v13a1 1 0 0 1-1 1H7a2 2 0 0 1-2-2V4Z" stroke="currentColor" stroke-width="1.8" />
						<path d="M8 9h7M8 13h7" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" />
					</svg>
				</span>
				<div>
					<p class="benefit__name">История обращений</p>
					<p class="benefit__text">Устройства, выполненные работы и стоимость — всегда под рукой.</p>
				</div>
			</article>
			<article class="benefit">
				<span class="benefit__icon" aria-hidden="true">
					<svg width="20" height="20" viewBox="0 0 24 24" fill="none">
						<path d="M4 5h16v11H9l-5 4V5Z" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round" />
						<path d="M9 9h6M9 12h3" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" />
					</svg>
				</span>
				<div>
					<p class="benefit__name">Новая заявка без лишних шагов</p>
					<p class="benefit__text">Покажем предварительную стоимость до визита. Ремонт начнём только после согласования.</p>
				</div>
			</article>
		</div>
	</div>
</section>

<style>
	.auth__grid {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 48px;
		align-items: start;
	}

	.card {
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

	.form-title {
		font-size: 18px;
		font-weight: 600;
		margin-top: 26px;
	}

	.form-note {
		font-size: 13px;
		color: var(--muted);
		margin-top: 4px;
	}

	.form-note strong {
		color: var(--text);
	}

	.field {
		display: flex;
		flex-direction: column;
		gap: 6px;
		margin-top: 18px;
	}

	.label {
		font-size: 14px;
		font-weight: 500;
	}

	.input {
		background: var(--bg);
		border: 1px solid var(--border);
		border-radius: 10px;
		padding: 12px 14px;
		font-size: 15px;
		transition: border-color 0.15s ease;
	}

	.input:focus {
		border-color: var(--accent);
		outline: none;
	}

	.input--error {
		border-color: var(--danger);
	}

	.code-group {
		display: grid;
		grid-template-columns: repeat(6, 1fr);
		gap: 10px;
		margin-top: 20px;
		max-width: 360px;
	}

	.code-input {
		aspect-ratio: 3 / 4;
		text-align: center;
		font-size: 28px;
		font-weight: 600;
		background: var(--bg);
		border: 1px solid var(--border);
		border-radius: 10px;
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

	.hint {
		margin-top: 18px;
		font-size: 12px;
		color: var(--muted);
	}

	.code-links {
		margin-top: 18px;
		display: flex;
		flex-direction: column;
		gap: 8px;
		font-size: 13px;
	}

	.link-muted {
		color: var(--muted);
	}

	.action {
		font-size: 13px;
		color: var(--accent);
		text-align: left;
	}

	.action:hover {
		text-decoration: underline;
		text-underline-offset: 3px;
	}

	.benefits {
		display: flex;
		flex-direction: column;
		gap: 18px;
		margin-top: 12px;
	}

	.benefits__title {
		font-size: 22px;
		font-weight: 700;
		letter-spacing: -0.02em;
		margin-bottom: 4px;
	}

	.benefit {
		display: flex;
		gap: 14px;
		background: var(--surface);
		border: 1px solid var(--border);
		border-radius: 16px;
		padding: 18px 20px;
	}

	.benefit__icon {
		width: 42px;
		height: 42px;
		border-radius: 10px;
		background: var(--accent-soft);
		color: var(--accent);
		display: inline-flex;
		align-items: center;
		justify-content: center;
		flex-shrink: 0;
	}

	.benefit__name {
		font-size: 14px;
		font-weight: 600;
	}

	.benefit__text {
		font-size: 13px;
		color: var(--muted);
		margin-top: 3px;
	}

	@media (max-width: 1024px) {
		.auth__grid {
			grid-template-columns: 1fr;
		}
	}

	@media (max-width: 480px) {
		.card {
			padding: 28px 20px;
		}
	}
</style>
