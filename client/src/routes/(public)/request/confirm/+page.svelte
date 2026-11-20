<script lang="ts">
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import Eyebrow from '#lib/components/Eyebrow.svelte';
	import Button from '#lib/components/Button.svelte';
	import { session } from '#lib/auth/session.svelte.ts';
	import { isPhone } from '../../../../services/auth.ts';
	import { fetchMyOrderById, isMockMode, ApiError } from '../../../../services/api.ts';

	// Шаг подтверждения живёт в sessionStorage: телефон и одноразовая ссылка
	// не попадают в историю браузера и access-логи; очищается после
	// завершения или истечения срока.
	interface ConfirmStep {
		phone: string;
		linkUrl: string | null;
		linkExpiresAtUtc: string | null;
		orderId: string;
		orderNumber: string;
	}
	const STEP_KEY = 'pps-confirm-step';

	function readStep(): ConfirmStep | null {
		try {
			const raw = sessionStorage.getItem(STEP_KEY);
			if (raw) {
				const step = JSON.parse(raw) as ConfirmStep;
				if (step.phone && step.orderId) return step;
			}
		} catch {
			// повреждённое состояние — считаем шаг недействительным
		}
		return null;
	}

	function clearStep(): void {
		sessionStorage.removeItem(STEP_KEY);
	}

	const orderId = $derived(page.url.searchParams.get('id') ?? '');
	const orderNumber = $derived(page.url.searchParams.get('number') ?? '');
	const step = readStep();

	const phone = $derived(step && isPhone(step.phone) ? step.phone : '');
	const messengerUrl = $derived(
		(isMockMode && phone ? 'https://vk.me/pro_phone_demo' : (step?.linkUrl ?? ''))
	);
	const linkExpiresAt = $derived(step?.linkExpiresAtUtc ? new Date(step.linkExpiresAtUtc) : null);

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
	}

	function onKeyDown(event: KeyboardEvent, index: number): void {
		if (event.key === 'Backspace' && !(event.currentTarget as HTMLInputElement).value) {
			code = code.slice(0, index) + code.slice(index + 1);
			inputs[index - 1]?.focus();
		}
	}

	// Дедлайн — серверный expiresAtUtc ссылки привязки (не локальный старт):
	// после перезагрузки отсчёт не начинается заново. Тикает интервал,
	// потому что Date.now() внутри $derived сам не пересчитывается.
	const deadlineMs = $derived(
		linkExpiresAt && !Number.isNaN(linkExpiresAt.getTime()) ? linkExpiresAt.getTime() : 0
	);
	let leftSeconds = $state(0);

	$effect(() => {
		if (!deadlineMs) return;
		leftSeconds = Math.max(0, Math.round((deadlineMs - Date.now()) / 1000));
		const timer = setInterval(() => {
			leftSeconds = Math.max(0, Math.round((deadlineMs - Date.now()) / 1000));
		}, 1000);
		return () => clearInterval(timer);
	});

	const linkExpired = $derived(deadlineMs > 0 && leftSeconds <= 0);

	const leftText = $derived(
		`${Math.floor(leftSeconds / 60)}:${String(leftSeconds % 60).padStart(2, '0')}`
	);

	function validate(): boolean {
		formError = '';
		if (!orderId || !phone) {
			clearStep();
			goto('/request');
			return false;
		}
		if (!/^\d{6}$/.test(code)) {
			formError = 'Введите 6 цифр из VK-сообщения.';
			return false;
		}
		return true;
	}

	async function confirm(): Promise<void> {
		if (busy || !validate()) return;
		busy = true;
		try {
			const authSession = await session.confirm(phone, code);
			if (authSession.role === 'Master') {
				clearStep();
				goto('/master/orders');
				return;
			}
			// Повторный код мог привязать прежний аккаунт: заявка остаётся
			// PendingContact — success показываем только при активации.
			try {
				const order = await fetchMyOrderById(orderId);
				if (order.status === null || order.status === 'PendingContact') {
					formError = 'Заявка не активирована этим кодом. Оформите заявку заново.';
					return;
				}
			} catch (error) {
				const notFound = error instanceof ApiError && error.status === 404;
				formError = notFound
					? 'Заявка не активирована этим кодом. Оформите заявку заново.'
					: error instanceof Error ? error.message : 'Не удалось проверить заявку.';
				return;
			}
			clearStep();
			// Заявка активирована: ведём в ЛК с номером и тайм-лайном.
			goto(`/request/success?id=${encodeURIComponent(orderId)}&number=${encodeURIComponent(orderNumber)}`);
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
			await session.requestCode(phone);
			startResendTimer();
		} catch (error) {
			formError = error instanceof Error ? error.message : 'Не удалось отправить код повторно.';
		}
	}
</script>

<section class="container section confirm">
	<div class="confirm__grid">
		<div class="card">
			<Eyebrow>Заявка {orderNumber ? `№${orderNumber}` : 'принята'}</Eyebrow>
			<h1 class="title">Осталось ввести код</h1>
			<p class="lead">
				Мы отправили одноразовый код в VK на <strong>{phone || 'ваш номер'}</strong>.
				Введите его — заявка активируется, и вы отследите её в личном кабинете.
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

		<aside class="messenger">
			<div class="messenger__card">
				<h2 class="messenger__title">Что в мессенджере</h2>
				<ol class="messenger__steps">
					<li>Откройте диалог сообщества «про телефон» в VK.</li>
					<li>Отправьте любое сообщение боту — это привяжет ваш телефон.</li>
					<li>Получите код и введите его на этом экране.</li>
				</ol>

				{#if messengerUrl && !linkExpired}
					<a
						class="messenger__open"
						href={messengerUrl}
						target="_blank"
						rel="noopener noreferrer"
					>
						<svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true">
							<path
								d="M12 2C6.48 2 2 6.48 2 12c0 5 3.66 9.13 8.44 9.88v-6.99H7.9V12h2.54V9.8c0-2.5 1.49-3.89 3.77-3.89 1.09 0 2.23.19 2.23.19v2.46h-1.26c-1.24 0-1.63.77-1.63 1.56V12h2.78l-.45 2.89h-2.33v6.99C18.34 21.13 22 17 22 12c0-5.52-4.48-10-10-10z"
								fill="currentColor"
							/>
						</svg>
						Открыть VK
					</a>
				{:else}
					<!-- Истёкшая ссылка без href: клик не откроет мёртвый токен. -->
					<span class="messenger__open messenger__open--expired">
						Открыть VK
					</span>
					<p class="messenger__expired">
						Срок действия ссылки истёк — оформите заявку заново.
					</p>
					<Button
						type="button"
						variant="secondary"
						onclick={() => {
							clearStep();
							goto('/request');
						}}
					>
						Новая заявка
					</Button>
				{/if}

				<div class="messenger__note">
					<strong>Время на подтверждение: {leftText}</strong>
					<p>
						Если не подтвердить заявку за отведённое время — она автоматически отменится.
						Отдельное сообщение об отмене не придёт, просто оформите заявку заново.
					</p>
				</div>
			</div>
		</aside>
	</div>
</section>

<style>
	.confirm {
		display: flex;
		justify-content: center;
		padding-block: 40px 72px;
	}

	.confirm__grid {
		display: grid;
		grid-template-columns: minmax(0, 1.2fr) minmax(0, 1fr);
		gap: 24px;
		align-items: start;
		max-width: 980px;
		width: 100%;
	}

	.card,
	.messenger__card {
		background: var(--surface);
		border: 1px solid var(--border);
		border-radius: 24px;
		padding: 36px;
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

	.messenger__title {
		font-size: 18px;
		font-weight: 700;
	}

	.messenger__steps {
		margin: 14px 0 0;
		padding-left: 18px;
		display: grid;
		gap: 10px;
		color: var(--muted);
		font-size: 14px;
		line-height: 1.5;
	}

	.messenger__open {
		margin-top: 18px;
		display: inline-flex;
		align-items: center;
		gap: 10px;
		padding: 12px 22px;
		border-radius: 12px;
		background: var(--accent);
		color: #fff;
		font-size: 14px;
		font-weight: 600;
		text-decoration: none;
	}

	.messenger__open:hover {
		filter: brightness(1.08);
	}

	.messenger__note {
		margin-top: 18px;
		background: var(--warning-soft);
		border-radius: 12px;
		padding: 12px 16px;
		font-size: 13px;
		line-height: 1.5;
	}

	.messenger__note strong {
		display: block;
		margin-bottom: 4px;
	}

	.messenger__note p {
		color: var(--muted);
	}

	.messenger__open--expired {
		background: var(--bg);
		color: var(--muted);
	}

	.messenger__expired {
		margin-top: 12px;
		font-size: 13px;
		color: var(--danger);
	}

	@media (max-width: 720px) {
		.confirm__grid {
			grid-template-columns: 1fr;
		}
	}
</style>
