<script lang="ts">
	import { goto } from '$app/navigation';
	import Eyebrow from '#lib/components/Eyebrow.svelte';
	import Button from '#lib/components/Button.svelte';
	import { session } from '#lib/auth/session.svelte.ts';
	import { showToast } from '#lib/components/toast.svelte.ts';
	import {
		devices,
		workshop,
		CUSTOM_DEVICE,
		serviceMeta,
		serviceOrderIndex,
		workshopWallTimeToUtcIso
	} from '../../../mocks/db.ts';
	import {
		ApiError,
		createOrder,
		fetchServices,
		isMockMode,
		isOrderActiveForClient,
		type CreateOrderPayload,
		type CreateOrderResult,
		type ServiceDto
	} from '../../../services/api.ts';

	// ── Form state ─────────────────────────────────────────────────────────
	let clientName = $state('');
	let deviceId = $state('');
	let customManufacturer = $state('');
	let customDevice = $state('');
	let selectedServiceIds = $state<string[]>([]);
	let contact = $state('');
	/** Способ связи из макета V4: Телефон | VK (VK = подтверждение через бота). */
	let contactChannel = $state<'phone' | 'vk'>('phone');
	let problemDescription = $state('');
	let visitDate = $state('');
	let visitTime = $state('');
	let services = $state<ServiceDto[]>([]);
	let servicesFailed = $state(false);
	let touched = $state<Record<string, boolean>>({});
	let apiFieldErrors = $state<Record<string, string>>({});
	let submitting = $state(false);
	/** Утверждённый сценарий недоступности мессенджера: повторить позже или оформить лично. */
	let messengerUnavailable = $state(false);
	// VK-флоу (карточка 10): заявка создаётся по кнопке «Подключить VK»,
	// далее в форме открывается блок привязки и ввода кода из VK.
	let vkPhase = $state<'idle' | 'creating' | 'awaiting' | 'confirming'>('idle');
	let vkOrderId = $state('');
	let vkOrderNumber = $state('');
	let vkPhone = $state('');
	let vkLink = $state<{ url: string; expiresAtUtc: string } | null>(null);
	/** Серверный дедлайн привязки (expiresAtUtc) в тиках; двигает таймер. */
	let nowTick = $state(Date.now());
	let linkTimerId: ReturnType<typeof setInterval> | null = null;
	$effect(() => {
		if (linkTimerId) return;
		linkTimerId = setInterval(() => (nowTick = Date.now()), 1000);
		return () => {
			if (linkTimerId) clearInterval(linkTimerId);
			linkTimerId = null;
		};
	});
	let vkLinkFresh = $derived(!!vkLink && new Date(vkLink.expiresAtUtc).getTime() > nowTick);
	/** Остаток до серверного дедлайна — тикает, потому что nowTick меняется. */
	let vkCode = $state('');
	let vkBusy = $state(false);
	let vkError = $state('');
	let vkInputs: HTMLInputElement[] = $state([]);
	let vkResendAfter = $state(60);
	let vkResendTimerId: ReturnType<typeof setInterval> | null = null;


	const device = $derived(devices.find((item) => item.id === deviceId) ?? null);
	const deviceManufacturer = $derived(
		device ? device.brand : deviceId === CUSTOM_DEVICE ? customManufacturer.trim() : ''
	);
	const deviceModel = $derived(
		device ? device.model : deviceId === CUSTOM_DEVICE ? customDevice.trim() : ''
	);
	const deviceName = $derived([deviceManufacturer, deviceModel].filter(Boolean).join(' '));

	const catalog = $derived(
		[...services].sort((a, b) => serviceOrderIndex(a.name) - serviceOrderIndex(b.name))
	);
	const selectedServices = $derived(services.filter((item) => selectedServiceIds.includes(item.id)));
	const estimateTotal = $derived(selectedServices.reduce((sum, item) => sum + item.price, 0));
	const selectedCount = $derived(selectedServices.length);

	$effect(() => {
		fetchServices()
			.then((items) => {
				services = items;
				servicesFailed = false;
			})
			.catch(() => {
				servicesFailed = true;
				showToast('Не удалось загрузить список услуг. Проверьте подключение к API.', 'error');
			});
	});

	// ── Validation: mirrors the backend (phone is the only contact) ────────
	const phoneDigits = $derived(contact.replace(/\D/g, ''));
	const phoneValid = $derived(
		/^\+7\d{10}$/.test(`+7${phoneDigits.slice(1)}`) && phoneDigits.length === 11
	);

	const contactValid = $derived(phoneValid);

	const visitTimes = $derived(
		Array.from(
			{ length: Number(workshop.closeAt.slice(0, 2)) - Number(workshop.openAt.slice(0, 2)) + 1 },
			(_, index) => `${String(Number(workshop.openAt.slice(0, 2)) + index).padStart(2, '0')}:00`
		)
	);
	const minimumVisitDate = $derived(
		new Intl.DateTimeFormat('en-CA', {
			timeZone: workshop.timeZone,
			year: 'numeric',
			month: '2-digit',
			day: '2-digit'
		}).format(new Date())
	);
	const currentWorkshopTime = $derived(
		new Intl.DateTimeFormat('en-GB', {
			timeZone: workshop.timeZone,
			hour: '2-digit',
			minute: '2-digit',
			hourCycle: 'h23'
		}).format(new Date())
	);
	const visitError = $derived((() => {
		if (!visitDate) return null;
		if (
			visitDate < minimumVisitDate ||
			(visitDate === minimumVisitDate && visitTime && visitTime <= currentWorkshopTime)
		) {
			return 'Выбранное время визита уже прошло';
		}
		const weekday = new Date(`${visitDate}T12:00:00Z`).getUTCDay();
		const isoWeekday = weekday === 0 ? 7 : weekday;
		if (!workshop.workingDays.includes(isoWeekday)) return 'Мастерская в этот день закрыта (выходной)';

		return null;
	})());

	const formValid = $derived(
		clientName.trim().length > 0 &&
		deviceName.length > 0 &&
		(deviceId !== CUSTOM_DEVICE || deviceManufacturer.length > 0) &&
		selectedServiceIds.length > 0 &&
		contactValid &&
		visitDate !== '' &&
		visitTime !== '' &&
		!visitError
	);

	/** Кнопка «Отправить заявку» активна только в телефонном сценарии. */
	const submitEnabled = $derived(formValid && !submitting && contactChannel === 'phone');
	/** «Подключить VK» работает на валидной форме. */
	const vkConnectEnabled = $derived(formValid && !submitting && !vkBusy && vkPhase !== 'awaiting' && vkPhase !== 'confirming');

	function validateField(name: string): string | null {
		switch (name) {
			case 'name':
				return clientName.trim().length > 0 ? null : 'Укажите ваше имя';
			case 'device':
				return deviceName.length === 0 || (deviceId === CUSTOM_DEVICE && !deviceManufacturer)
					? 'Укажите производителя и модель устройства'
					: null;
			case 'services':
				return selectedServiceIds.length > 0 ? null : 'Выберите хотя бы одну услугу';
			case 'contact':
				return contactValid ? null : 'Введите телефон в формате +7 (XXX) XXX-XX-XX';
			case 'visit':
				if (!visitDate) return 'Выберите дату визита';
				if (!visitTime) return 'Выберите время визита';

				return visitError;
			default:
				return null;
		}
	}

	function fieldError(name: string): string | null {
		return touched[name] ? (apiFieldErrors[name] ?? validateField(name)) : null;
	}

	function markTouched(name: string): void {
		touched[name] = true;
		delete apiFieldErrors[name];
	}

	// ── Contact mask: keeps emails as typed, masks phone numbers ───────────
	function formatPhone(raw: string): string {
		let digits = raw.replace(/\D/g, '');
		if (digits.startsWith('8')) digits = `7${digits.slice(1)}`;
		if (!digits.startsWith('7')) digits = `7${digits}`;
		digits = digits.slice(0, 11);
		const rest = digits.slice(1);
		let output = '+7';
		if (rest.length > 0) output += ` (${rest.slice(0, 3)}`;
		if (rest.length >= 3) output += ')';
		if (rest.length > 3) output += ` ${rest.slice(3, 6)}`;
		if (rest.length > 6) output += `-${rest.slice(6, 8)}`;
		if (rest.length > 8) output += `-${rest.slice(8, 10)}`;

		return output;
	}

	function onContactInput(event: Event): void {
		contact = formatPhone((event.target as HTMLInputElement).value);
	}

	function toggleService(id: string): void {
		selectedServiceIds = selectedServiceIds.includes(id)
			? selectedServiceIds.filter((item) => item !== id)
			: [...selectedServiceIds, id];
		touched.services = true;
	}

	// ── Estimate helpers ───────────────────────────────────────────────────
	const priceFormat = new Intl.NumberFormat('ru-RU');

	function priceText(price: number): string {
		return price === 0 ? 'Бесплатно' : `от ${priceFormat.format(price)} ₽`;
	}

	function plural(count: number, forms: [string, string, string]): string {
		const mod10 = count % 10;
		const mod100 = count % 100;
		if (mod10 === 1 && mod100 !== 11) return forms[0];
		if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return forms[1];

		return forms[2];
	}

	// ── VK-флоу: подключить VK до отправки заявки ───────────────────────────
	async function connectVk(): Promise<void> {
		if (!vkConnectEnabled) return;
		for (const field of ['name', 'device', 'services', 'contact', 'visit']) touched[field] = true;
		if (!formValid) return;

		vkPhase = 'creating';
		vkError = '';
		const orderPhone = `+7${phoneDigits.slice(1)}`;
		try {
			const payload: CreateOrderPayload = {
				name: clientName.trim(),
				phone: orderPhone,
				deviceType: 'Phone',
				deviceManufacturer,
				deviceModel,
				preferredVisitAt: workshopWallTimeToUtcIso(visitDate, visitTime),
				serviceIds: selectedServiceIds,
				problemDescription: problemDescription.trim() || null
			};
			const result = await createOrder(payload);
			vkOrderId = result.id;
			vkOrderNumber = result.orderNumber;
			vkPhone = orderPhone;
			// Авторизованный клиент получает заявку сразу: подтверждение не
			// требуется, ссылка null — ведём на success после проверки активации.
			if (!result.requiresContactConfirmation) {
				vkPhase = 'idle';
				const activated = await isOrderActiveForClient(vkOrderId);
				if (activated) {
					await goto(
						`/request/success?id=${encodeURIComponent(vkOrderId)}&number=${encodeURIComponent(vkOrderNumber)}`
					);
				} else {
					vkError = 'Заявка не активирована. Оформите заявку заново.';
				}
				return;
			}
			if (!result.messengerLink) {
				vkPhase = 'idle';
				messengerUnavailable = true;
				return;
			}
			vkLink = { url: result.messengerLink.url, expiresAtUtc: result.messengerLink.expiresAtUtc };
			vkPhase = 'awaiting';
			startVkResendTimer();
			if (isMockMode) showToast('Демо-режим: подойдёт любой 6-значный код из VK', 'info');
		} catch (error) {
			// При ошибке фаза возвращается в idle (панель не открывается),
			// ошибка показывается над кнопкой; vkPhase='awaiting' из try не трогаем.
			vkPhase = 'idle';
			if (error instanceof ApiError && error.status === 503) {
				messengerUnavailable = true;
			} else {
				vkError = error instanceof Error ? error.message : 'Не удалось начать подключение VK.';
			}
		}
	}

	function openVk(): void {
		if (!vkLink?.url) return;
		window.open(vkLink.url, '_blank', 'noopener');
	}

	function onVkCodeInput(event: Event, index: number): void {
		const input = event.currentTarget as HTMLInputElement;
		const clean = input.value.replace(/\D/g, '').slice(-1);
		input.value = clean;
		vkCode = vkCode.slice(0, index) + clean + vkCode.slice(index + 1);
		if (clean && index < 5) vkInputs[index + 1]?.focus();
	}

	function onVkKeyDown(event: KeyboardEvent, index: number): void {
		if (event.key === 'Backspace' && !(event.currentTarget as HTMLInputElement).value) {
			vkCode = vkCode.slice(0, index) + vkCode.slice(index + 1);
			vkInputs[index - 1]?.focus();
		}
	}

	function startVkResendTimer(): void {
		vkResendAfter = 60;
		if (vkResendTimerId) clearInterval(vkResendTimerId);
		vkResendTimerId = setInterval(() => {
			vkResendAfter -= 1;
			if (vkResendAfter <= 0 && vkResendTimerId) {
				clearInterval(vkResendTimerId);
				vkResendTimerId = null;
			}
		}, 1000);
	}

	async function confirmVk(): Promise<void> {
		if (!/^\d{6}$/.test(vkCode) || vkBusy) return;
		vkBusy = true;
		vkError = '';
		try {
			const authSession = await session.confirm(vkPhone || `+7${phoneDigits.slice(1)}`, vkCode);
			if (authSession.role === 'Master') {
				goto('/master/orders');
				return;
			}
			// Повторный код мог авторизовать прежний аккаунт: новая гостевая
			// заявка остаётся PendingContact. В success идём только если
			// конкретная заявка доступна клиенту и активирована.
			const activated = await isOrderActiveForClient(vkOrderId);
			if (!activated) {
				vkError = 'Заявка не активирована этим кодом. Оформите заявку заново.';
				return;
			}
			await goto(
				`/request/success?id=${encodeURIComponent(vkOrderId)}&number=${encodeURIComponent(vkOrderNumber)}`
			);
		} catch (error) {
			vkError = error instanceof Error ? error.message : 'Неверный код. Попробуйте ещё раз.';
			vkCode = '';
			vkInputs[0]?.focus();
		} finally {
			vkBusy = false;
		}
	}

	async function resendVkCode(): Promise<void> {
		if (vkResendAfter > 0 || vkBusy) return;
		if (isMockMode) {
			startVkResendTimer();
			return;
		}
		try {
			await session.requestCode(`+7${phoneDigits.slice(1)}`);
			startVkResendTimer();
		} catch (error) {
			vkError = error instanceof Error ? error.message : 'Не удалось отправить код повторно.';
		}
	}

	async function submit(): Promise<void> {
		for (const field of ['name', 'device', 'services', 'contact', 'visit']) touched[field] = true;
		if (!formValid || submitting) return;

		submitting = true;
		let result: CreateOrderResult;
		const orderPhone = `+7${phoneDigits.slice(1)}`;
		try {
			const payload: CreateOrderPayload = {
				name: clientName.trim(),
				phone: orderPhone,
				deviceType: 'Phone',
				deviceManufacturer,
				deviceModel,
				preferredVisitAt: workshopWallTimeToUtcIso(visitDate, visitTime),
				serviceIds: selectedServiceIds,
				problemDescription: problemDescription.trim() || null
			};
			result = await createOrder(payload);
		} catch (error) {
			const fieldMap: Record<string, string> = {
				Name: 'name',
				Phone: 'contact',
				Email: 'contact',
				DeviceType: 'device',
				DeviceManufacturer: 'device',
				DeviceModel: 'device',
				ServiceIds: 'services',
				PreferredVisitAt: 'visit'
			};
			let mappedFieldError = false;
			if (error instanceof ApiError) {
				for (const [backendField, messages] of Object.entries(error.fieldErrors)) {
					const formField = fieldMap[backendField];
					if (!formField || !messages[0]) continue;
					apiFieldErrors[formField] = messages[0];
					touched[formField] = true;
					mappedFieldError = true;
				}
			}
			if (!mappedFieldError) {
				if (error instanceof ApiError && error.status === 503) {
					// Мессенджер недоступен (утверждённый текст из карточки 14):
					// повторить попытку позже или оформить заказ лично.
					messengerUnavailable = true;
				} else {
					showToast(
						error instanceof Error ? error.message : 'Не удалось отправить заявку. Попробуйте ещё раз.',
						'error'
					);
				}
			}
			submitting = false;

			return;
		}

		// Card 10: любой PendingContact-результат идёт через VK-подтверждение.
		// В телефонном сценарии — отдельный экран кода (макет V4, слева код /
		// справа «Что в мессенджере»); VK-сценарий обрабатывается в форме.
		if (result.requiresContactConfirmation) {
			const param = new URLSearchParams({
				id: result.id,
				number: result.orderNumber,
				...(result.messengerLink
					? {
							link: result.messengerLink.url,
							expires: result.messengerLink.expiresAtUtc
						}
					: {})
			});
			await goto(`/request/confirm?${param}`);
			return;
		}

		const query = new URLSearchParams({
			id: result.id,
			device: deviceName,
			services: selectedServices.map((item) => serviceMeta(item.name).displayName).join(', '),
			price: String(estimateTotal),
			date: visitDate,
			time: visitTime
		});
		await goto(`/request/success?${query}`);
	}
</script>

<section class="container section">
	<Eyebrow>Без звонков и лишних вопросов</Eyebrow>
	<h1 class="h2 heading">Давайте вернём телефон в строй</h1>
	<p class="lead">
		Расскажите, что случилось. Покажем ориентировочную цену и подготовимся к вашему визиту.
	</p>

	<div class="grid">
		<!-- ─────────── Left column: form ─────────── -->
		<form class="form" novalidate onsubmit={(e) => { e.preventDefault(); submit(); }}>
			<div class="form-head">
				<h2 class="form-title">Устройство и проблема</h2>
				<span class="required-note">Все поля обязательны</span>
			</div>

			<div class="field">
				<label class="label" for="clientName">Ваше имя</label>
				<input
					id="clientName"
					class="input"
					class:input--error={!!fieldError('name')}
					autocomplete="name"
					bind:value={clientName}
					onblur={() => markTouched('name')}
				/>
				{#if fieldError('name')}<span class="error" role="alert">{fieldError('name')}</span>{/if}
			</div>

			<div class="field">
				<label class="label" for="device">Модель устройства</label>
				<select
					id="device"
					class="input"
					class:input--error={!!fieldError('device')}
					bind:value={deviceId}
					onblur={() => markTouched('device')}
				>
					<option value="" disabled selected>Выберите модель</option>
					{#each devices as item (item.id)}
						<option value={item.id}>{item.brand} {item.model}</option>
					{/each}
					<option value={CUSTOM_DEVICE}>Нет модели в списке? Указать вручную</option>
				</select>
				{#if deviceId === CUSTOM_DEVICE}
					<label class="label" for="customManufacturer">Производитель</label>
					<input
						id="customManufacturer"
						class="input"
						class:input--error={!!fieldError('device')}
						placeholder="Например: Google"
						bind:value={customManufacturer}
						onblur={() => markTouched('device')}
					/>
					<label class="label" for="customDevice">Модель</label>
					<input
						id="customDevice"
						class="input"
						class:input--error={!!fieldError('device')}
						placeholder="Например: Pixel 8"
						bind:value={customDevice}
						onblur={() => markTouched('device')}
					/>
				{/if}
				{#if fieldError('device')}
					<span class="error" role="alert">{fieldError('device')}</span>
				{/if}
			</div>

			<div class="field">
				<span class="label">Выберите услуги</span>
				<span class="hint">Можно выбрать несколько услуг</span>
				<ul class="services" class:services--error={!!fieldError('services')}>
					{#each catalog as service (service.id)}
						<li>
							<label class="service" class:service--selected={selectedServiceIds.includes(service.id)}>
								<input
									type="checkbox"
									checked={selectedServiceIds.includes(service.id)}
									onchange={() => toggleService(service.id)}
									onblur={() => markTouched('services')}
								/>
								<span class="service__name">{serviceMeta(service.name).displayName}</span>
								<span class="service__price">{priceText(service.price)}</span>
								<span class="service__check" aria-hidden="true">✓</span>
							</label>
						</li>
					{:else}
						<li class="service service--muted">
							{servicesFailed ? 'Список услуг недоступен.' : 'Загружаем услуги…'}
						</li>
					{/each}
				</ul>
				{#if fieldError('services')}
					<span class="error" role="alert">{fieldError('services')}</span>
				{/if}
			</div>

			<div class="field-row">
				<div class="field">
					<label class="label" for="visit">Дата визита</label>
					<input
						id="visit"
						class="input"
						class:input--error={!!fieldError('visit')}
						type="date"
						min={minimumVisitDate}
						bind:value={visitDate}
						onblur={() => markTouched('visit')}
					/>
					<span class="hint">Выберите удобный день для визита в мастерскую.</span>
					{#if fieldError('visit')}
						<span class="error" role="alert">{fieldError('visit')}</span>
					{/if}
				</div>
				<div class="field">
					<label class="label" for="visitTime">Время визита</label>
					<select id="visitTime" class="input" bind:value={visitTime} onblur={() => markTouched('visit')}>
						<option value="" disabled selected>Выберите время</option>
						{#each visitTimes as time (time)}<option value={time}>{time}</option>{/each}
					</select>
				</div>
			</div>

			<div class="field">
				<label class="label" for="description">Описание проблемы</label>
				<textarea
					id="description"
					class="input input--area"
					rows="3"
					placeholder="Телеграфным стилем: что случилось, состояние устройства"
					maxlength="2000"
					bind:value={problemDescription}
				></textarea>
				<span class="hint">Необязательно — максимум 2000 символов.</span>
			</div>

			<div class="field">
				<p class="label" id="channel-label">Способ связи</p>
				<div class="contact-tabs" role="tablist" aria-labelledby="channel-label">
					<button
						type="button"
						class="contact-tab"
						class:contact-tab--active={contactChannel === 'phone'}
						role="tab"
						aria-selected={contactChannel === 'phone'}
						onclick={() => (contactChannel = 'phone')}
					>
						Телефон
					</button>
					<button
						type="button"
						class="contact-tab"
						class:contact-tab--active={contactChannel === 'vk'}
						role="tab"
						aria-selected={contactChannel === 'vk'}
						onclick={() => (contactChannel = 'vk')}
					>
						VK
					</button>
				</div>
			</div>

			<div class="field">
				<label class="label" for="contact">{contactChannel === 'vk' ? 'VK на этот номер' : 'Номер телефона'}</label>
				<input
					id="contact"
					class="input"
					class:input--error={!!fieldError('contact')}
					inputmode="tel"
					placeholder="+7 (916) 234-56-78"
					value={contact}
					oninput={onContactInput}
					onblur={() => markTouched('contact')}
				/>
				<span class="hint">
					{contactChannel === 'vk'
						? 'Код подтверждения придёт в VK-сообщения этого номера. Заявка уйдёт после подключения.'
						: 'Только для связи по ремонту. Никаких рекламных сообщений.'}
				</span>
				{#if fieldError('contact')}
					<span class="error" role="alert">{fieldError('contact')}</span>
				{/if}
			</div>

			{#if contactChannel === 'vk' && vkError && vkPhase === 'idle'}
				<p class="form-error" role="alert">{vkError}</p>
			{/if}

			{#if contactChannel === 'vk' && (vkPhase === 'awaiting' || vkPhase === 'confirming')}
				<div class="vk-panel" role="group" aria-label="Подключение VK">
					<div class="vk-panel__head">
						<p class="vk-panel__title">
							{vkOrderNumber ? `Заявка ${vkOrderNumber} — подключите VK` : 'Подключите VK'}
						</p>
						<p class="vk-panel__text">
							Откройте диалог сообщества и отправьте боту любое сообщение —
							он привяжет номер и пришлёт код. Введите код здесь.
						</p>
					</div>

					<div class="vk-panel__actions">
						{#if vkLink && vkLinkFresh}
							<button type="button" class="vk-panel__open" onclick={openVk}>
								<svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true">
									<path d="M12 2C6.48 2 2 6.48 2 12c0 5 3.66 9.13 8.44 9.88v-6.99H7.9V12h2.54V9.8c0-2.5 1.49-3.89 3.77-3.89 1.09 0 2.23.19 2.23.19v2.46h-1.26c-1.24 0-1.63.77-1.63 1.56V12h2.78l-.45 2.89h-2.33v6.99C18.34 21.13 22 17 22 12c0-5.52-4.48-10-10-10z" fill="currentColor"/>
								</svg>
								Открыть VK
							</button>
						{:else}
							<button
								type="button"
								class="vk-panel__open vk-panel__open--expired"
								onclick={() => {
									vkPhase = 'idle';
									vkError = 'Срок действия ссылки привязки истёк — оформите заявку заново.';
								}}
							>
								Ссылка истекла
							</button>
						{/if}
					</div>

					<div class="code-group vk-panel__code" aria-label="Код из 6 цифр">
						{#each ['v1', 'v2', 'v3', 'v4', 'v5', 'v6'] as c, i (c)}
							<input
								class="code-input"
								type="text"
								inputmode="numeric"
								maxlength="1"
								autocomplete="one-time-code"
								aria-label={"Цифра " + (i + 1)}
								oninput={(e) => onVkCodeInput(e, i)}
								onkeydown={(e) => onVkKeyDown(e, i)}
								bind:this={vkInputs[i]}
							/>
						{/each}
					</div>

					{#if vkError}
						<p class="form-error" role="alert">{vkError}</p>
					{/if}

					<div class="submit">
						<Button
							type="button"
							size="lg"
							disabled={!/^\d{6}$/.test(vkCode) || vkBusy}
							onclick={() => confirmVk()}
						>
							{vkBusy ? 'Подтверждаем…' : 'Отправить заявку'}
						</Button>
					</div>

					<div class="links">
						{#if isMockMode || vkResendAfter <= 0}
							<button class="link-action" type="button" onclick={resendVkCode}>Отправить код повторно</button>
						{:else}
							<span class="link-muted">Отправить код повторно через 00:{String(vkResendAfter).padStart(2, '0')}</span>
						{/if}
					</div>
				</div>
			{/if}

			{#if messengerUnavailable}
				<div class="offline-note" role="alert">
					<p class="offline-note__title">Мессенджер временно недоступен</p>
					<p class="offline-note__text">
						К сожалению, сейчас не удаётся отправить сообщение через мессенджер.
						Вы можете повторить попытку позже или оформить заявку лично в нашем
						пункте обслуживания ({workshop.address}).
					</p>
					<p class="offline-note__text">Приносим извинения за неудобства!</p>
					<button class="offline-note__retry" type="button" onclick={() => (messengerUnavailable = false)}>
						Повторить попытку
					</button>
				</div>
			{/if}

			{#if contactChannel === 'phone'}
				<Button type="submit" size="lg" disabled={!submitEnabled}>
					{submitting ? 'Отправляем…' : 'Отправить заявку'}
				</Button>
			{:else if vkPhase !== 'awaiting' && vkPhase !== 'confirming'}
				<Button
					type="button"
					size="lg"
					disabled={!vkConnectEnabled}
					onclick={() => connectVk()}
				>
					{vkPhase === 'creating' ? 'Подключаем…' : 'Подключить VK'}
				</Button>
			{/if}

			<p class="consent">
				Нажимая «Отправить заявку», вы соглашаетесь с обработкой персональных данных и
				<button
					type="button"
					class="consent-link"
					onclick={() => showToast('Политика конфиденциальности появится позже')}
				>политикой конфиденциальности</button>.
			</p>
		</form>

		<!-- ─────────── Right column: estimate ─────────── -->
		<aside class="estimate">
			<div class="estimate__card">
				<h2 class="estimate__title">Ориентировочная стоимость</h2>

				{#if deviceName}
					<p class="estimate__device">{deviceName}</p>
				{/if}
				<p class="estimate__count">
					{selectedCount > 0
						? `${selectedCount} ${plural(selectedCount, ['выбранная услуга', 'выбранные услуги', 'выбранных услуг'])}`
						: 'Услуги не выбраны'}
				</p>

				<dl class="estimate__lines">
					{#each selectedServices as service (service.id)}
						<div>
							<dt>{serviceMeta(service.name).displayName}</dt>
							<dd>{priceText(service.price)}</dd>
						</div>
					{/each}
					<div>
						<dt>Диагностика</dt>
						<dd>Бесплатно</dd>
					</div>
				</dl>

				<div class="estimate__total">
					<span class="estimate__total-label">Предварительная оценка</span>
					<span class="estimate__total-value">
						{selectedCount > 0 ? `от ${priceFormat.format(estimateTotal)} ₽` : '—'}
					</span>
				</div>
				<p class="estimate__duration">Обычно от 40 минут после диагностики</p>
			</div>

			<div class="estimate__note">
				<strong>Пока не подтверждённая цена.</strong>
				После диагностики мастер подтвердит стоимость. Начнём ремонт только после вашего согласия.
			</div>

			<div class="estimate__next">
				<h3>Что будет после отправки?</h3>
				<ol class="next-steps">
					<li>Вы получите номер заявки для отслеживания.</li>
					<li>Приносите телефон на {workshop.addressShort}.</li>
					<li>Мастер проведёт бесплатную диагностику.</li>
				</ol>
			</div>
		</aside>
	</div>
</section>

<style>
	.heading {
		margin-bottom: 12px;
	}

	.lead {
		color: var(--muted);
		max-width: 560px;
	}

	.grid {
		display: grid;
		grid-template-columns: 1.5fr 1fr;
		gap: 40px;
		margin-top: 40px;
		align-items: start;
	}

	.grid > :global(*) {
		/* let form/estimate shrink inside grid tracks on narrow screens */
		min-width: 0;
	}

	/* ── Form ── */
	.form-head {
		display: flex;
		align-items: baseline;
		justify-content: space-between;
		gap: 12px;
		margin-bottom: 20px;
	}

	.form-title {
		font-size: 20px;
		font-weight: 600;
	}

	.required-note {
		font-size: 12px;
		color: var(--muted);
	}

	.field {
		display: flex;
		flex-direction: column;
		gap: 6px;
		margin-bottom: 20px;
	}

	.field-row {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 16px;
	}

	.label {
		font-size: 14px;
		font-weight: 500;
	}

	.input {
		background: var(--surface);
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

.input--area {
		font-family: inherit;
		resize: vertical;
	}

		.input--error {
		border-color: var(--danger);
	}

	select.input {
		appearance: none;
		background-image: linear-gradient(45deg, transparent 49%, var(--muted) 50%),
			linear-gradient(-45deg, transparent 49%, var(--muted) 50%);
		background-size: 6px 6px;
		background-position:
			right 18px center,
			right 13px center;
		background-repeat: no-repeat;
		padding-right: 36px;
	}

	.hint {
		font-size: 12px;
		color: var(--muted);
	}

	.error {
		font-size: 12px;
		color: var(--danger);
	}

	/* ── Services multi-select ── */
	.services {
		display: flex;
		flex-direction: column;
		gap: 8px;
		margin-top: 6px;
		border: 1px solid var(--border);
		border-radius: 12px;
		padding: 8px;
		background: var(--surface);
	}

	.services--error {
		border-color: var(--danger);
	}

	.service {
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 10px 12px;
		border-radius: 10px;
		cursor: pointer;
		transition: background-color 0.15s ease;
	}

	.service:hover {
		background: var(--accent-soft);
	}

	.service--selected {
		background: var(--accent-soft);
	}

	.service--muted {
		color: var(--muted);
		cursor: default;
	}

	.service input {
		position: absolute;
		opacity: 0;
		pointer-events: none;
	}

	.service__name {
		flex: 1;
		font-size: 14px;
	}

	.service__price {
		font-size: 13px;
		color: var(--muted);
		white-space: nowrap;
	}

	.service__check {
		color: var(--accent);
		font-size: 15px;
		font-weight: 600;
		opacity: 0;
	}

	.service--selected .service__check {
		opacity: 1;
	}

	.consent {
		margin-top: 16px;
		font-size: 12px;
		color: var(--muted);
		max-width: 480px;
	}

	.consent-link {
		color: var(--accent);
		text-decoration: underline;
		text-underline-offset: 3px;
	}

	/* ── Estimate ── */
	.estimate {
		position: sticky;
		top: 96px;
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.estimate__card {
		background: var(--surface);
		border: 1px solid var(--border);
		border-radius: 16px;
		padding: 24px;
	}

	.estimate__title {
		font-size: 17px;
		font-weight: 600;
		margin-bottom: 16px;
	}

	.estimate__device {
		font-size: 15px;
		font-weight: 500;
	}

	.estimate__count {
		font-size: 13px;
		color: var(--muted);
		margin-top: 2px;
		margin-bottom: 12px;
	}

	.estimate__lines {
		display: flex;
		flex-direction: column;
		gap: 10px;
	}

	.estimate__lines div {
		display: flex;
		justify-content: space-between;
		gap: 12px;
	}

	.estimate__lines dt {
		font-size: 14px;
		color: var(--muted);
	}

	.estimate__lines dd {
		font-size: 14px;
		font-weight: 500;
		margin: 0;
		white-space: nowrap;
	}

	.estimate__total {
		display: flex;
		justify-content: space-between;
		align-items: baseline;
		gap: 12px;
		border-top: 1px solid var(--border);
		margin-top: 16px;
		padding-top: 16px;
	}

	.estimate__total-label {
		font-size: 11px;
		font-weight: 600;
		letter-spacing: 0.08em;
		text-transform: uppercase;
		color: var(--muted);
	}

	.estimate__total-value {
		font-size: 28px;
		font-weight: 600;
		white-space: nowrap;
	}

	.estimate__duration {
		font-size: 12px;
		color: var(--muted);
		margin-top: 8px;
	}

	.estimate__note {
		background: var(--warning-soft);
		border-radius: 12px;
		padding: 16px 18px;
		font-size: 13px;
		line-height: 1.55;
	}

	.estimate__next {
		background: var(--surface);
		border: 1px solid var(--border);
		border-radius: 16px;
		padding: 24px;
	}

	.estimate__next h3 {
		font-size: 15px;
		font-weight: 600;
		margin-bottom: 12px;
	}

	.next-steps {
		display: flex;
		flex-direction: column;
		gap: 10px;
		counter-reset: step;
	}

	.next-steps li {
		position: relative;
		font-size: 13px;
		color: var(--muted);
		padding-left: 32px;
	}

	.next-steps li::before {
		counter-increment: step;
		content: counter(step, decimal-leading-zero);
		position: absolute;
		left: 0;
		top: 1px;
		font-size: 12px;
		font-weight: 600;
		color: var(--accent);
	}

	/* ── Responsive ── */
	@media (max-width: 1024px) {
		.grid {
			grid-template-columns: 1fr;
		}

		.estimate {
			position: static;
		}
	}

	@media (max-width: 600px) {
		.field-row {
			grid-template-columns: 1fr;
		}
	}

	.offline-note {
		margin-top: 16px;
		background: var(--warning-soft);
		border-radius: 12px;
		padding: 14px 16px;
		font-size: 13px;
		line-height: 1.55;
	}

	.offline-note__title {
		font-weight: 700;
		margin-bottom: 6px;
	}

	.offline-note__text {
		color: var(--muted);
	}

	.offline-note__retry {
		margin-top: 10px;
		font-size: 13px;
		color: var(--accent);
	}

	.offline-note__retry:hover {
		text-decoration: underline;
		text-underline-offset: 3px;
	}

	.contact-tabs {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 6px;
		background: var(--bg);
		border: 1px solid var(--border);
		border-radius: 10px;
		padding: 5px;
	}

	.contact-tab {
		padding: 9px 14px;
		border-radius: 7px;
		font-size: 14px;
		font-weight: 500;
		color: var(--muted);
		transition: background 0.15s ease, color 0.15s ease;
	}

	.contact-tab:hover {
		color: var(--text);
	}

	.contact-tab--active {
		background: var(--accent);
		color: #fff;
	}

	.vk-panel {
		margin-top: 16px;
		background: var(--surface);
		border: 1px solid var(--border);
		border-radius: 16px;
		padding: 20px;
	}

	.vk-panel__title {
		font-size: 15px;
		font-weight: 700;
	}

	.vk-panel__text {
		color: var(--muted);
		font-size: 13px;
		line-height: 1.55;
		margin-top: 6px;
	}

	.vk-panel__actions {
		margin-top: 14px;
	}

	.vk-panel__open {
		display: inline-flex;
		align-items: center;
		gap: 10px;
		padding: 11px 20px;
		border-radius: 12px;
		background: var(--accent);
		color: #fff;
		font-size: 14px;
		font-weight: 600;
	}

	.vk-panel__open:disabled,
	.vk-panel__open--expired {
		opacity: 0.6;
	}

	.vk-panel__code {
		margin-top: 16px;
	}

	.links {
		margin-top: 12px;
		font-size: 13px;
	}

	.link-action {
		color: var(--accent);
	}

	.link-muted {
		color: var(--muted);
	}
</style>
