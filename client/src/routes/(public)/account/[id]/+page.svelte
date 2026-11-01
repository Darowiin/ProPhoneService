<script lang="ts">
	import { goto } from '$app/navigation';
	import Eyebrow from '#lib/components/Eyebrow.svelte';
	import StatusBadge from '#lib/components/StatusBadge.svelte';
	import { workshop } from '../../../../mocks/db.ts';
	import { session } from '#lib/auth/session.svelte.ts';
	import {
		fetchMyOrderById,
		type AccountOrderDto
	} from '../../../../services/api.ts';

	const orderId = $derived(pageUrlId());


	let order = $state<AccountOrderDto | null>(null);
	let notFound = $state(false);

	import { page } from '$app/state';
	function pageUrlId(): string {
		return page.params.id ?? '';
	}


	const priceFormat = new Intl.NumberFormat('ru-RU');

	$effect(() => {
		if (!session.ready) return;
		if (!session.user) {
			goto('/login?next=/account');
			return;
		}
		if (!orderId) {
			goto('/account');
			return;
		}
		fetchMyOrderById(orderId)
			.then((result) => {
				order = result;
			})
			.catch(() => (notFound = true));
	});

</script>

<section class="container section repair">
	{#if !session.ready || !session.user}
		<p class="state">Загружаем кабинет…</p>
	{:else if notFound}
		<p class="state">Ремонт не найден. <a class="repair__back" href="/account">Вернуться в кабинет</a></p>
	{:else if order}
		<a class="repair__back" href="/account">← Все ремонты</a>
		<Eyebrow>Карточка ремонта</Eyebrow>
		<div class="head">
			<h1 class="head__title">{order.device} · {order.repair}</h1>
			<StatusBadge status={order.status} />
		</div>

		<div class="grid">
			<div class="summary">
				<div class="summary__row">
					<span class="summary__label">Дата обращения</span>
					<span class="summary__value">{order.createdAt ? new Date(order.createdAt).toLocaleDateString('ru-RU', { day: '2-digit', month: 'long', year: 'numeric' }) : '—'}</span>
				</div>
				<div class="summary__row">
					<span class="summary__label">Услуги</span>
					<span class="summary__value">{order.repair}</span>
				</div>
				<div class="summary__row">
					<span class="summary__label">Стоимость</span>
					<span class="summary__value summary__value--price">{priceFormat.format(order.price)} ₽</span>
				</div>
				{#if order.expectedReady}
					<div class="summary__row">
						<span class="summary__label">Готовность</span>
						<span class="summary__value">{order.expectedReady.replace('Ожидаемая готовность: ', '')}</span>
					</div>
				{/if}
			</div>

			<div class="timeline-card">
				<p class="timeline-card__title">Этапы ремонта</p>
				<ol class="timeline">
					{#each order.timeline as step, i (step.label + i)}
						<li class="timeline__item" class:timeline__item--last={i === order.timeline.length - 1}>
							<span class="timeline__dot" aria-hidden="true"></span>
							<span class="timeline__label">{step.label}</span>
							<span class="timeline__date">{step.at}</span>
						</li>
					{/each}
				</ol>
			</div>
		</div>

		{#if order.status === 'Completed'}
			<section class="review" aria-label="Отзыв о ремонте">
				<h2 class="review__title">Как всё прошло?</h2>
				<p class="review__done">
					Расскажите о ремонте на Яндекс.Картах — это помогает другим найти мастерскую и держит нас в тонусе.
				</p>
				<a
					class="review__link"
					href="https://yandex.ru/maps/?text={encodeURIComponent(workshop.address)}"
					target="_blank"
					rel="noreferrer"
				>
					Оставить отзыв на Яндекс.Картах ↗
				</a>
			</section>
		{/if}
	{/if}
</section>

<style>
	.repair__back {
		display: inline-block;
		font-size: 13px;
		font-weight: 500;
		color: var(--muted);
		margin-bottom: 16px;
	}

	.repair__back:hover {
		color: var(--text);
	}

	.head {
		display: flex;
		align-items: center;
		gap: 16px;
		margin-bottom: 28px;
	}

	.head__title {
		font-size: clamp(22px, 2.6vw, 30px);
		font-weight: 700;
		letter-spacing: -0.02em;
	}

	.grid {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 24px;
		align-items: start;
	}

	.summary,
	.timeline-card {
		background: var(--surface);
		border: 1px solid var(--border);
		border-radius: 16px;
		padding: 26px;
	}

	.summary__row {
		display: flex;
		justify-content: space-between;
		gap: 24px;
		padding-block: 10px;
	}

	.summary__row + .summary__row {
		border-top: 1px solid var(--border);
	}

	.summary__label {
		font-size: 13px;
		color: var(--muted);
	}

	.summary__value {
		font-size: 14px;
		font-weight: 500;
		text-align: right;
	}

	.summary__value--price {
		font-size: 16px;
		font-weight: 700;
	}

	.timeline-card__title {
		font-size: 16px;
		font-weight: 600;
		margin-bottom: 16px;
	}

	.timeline {
		display: flex;
		flex-direction: column;
	}

	.timeline__item {
		position: relative;
		display: flex;
		align-items: baseline;
		gap: 10px;
		padding-bottom: 18px;
		padding-left: 24px;
	}

	.timeline__item::before {
		content: '';
		position: absolute;
		left: 5px;
		top: 16px;
		bottom: -2px;
		width: 2px;
		background: var(--border);
	}

	.timeline__item--last::before {
		display: none;
	}

	.timeline__dot {
		position: absolute;
		left: 0;
		top: 6px;
		width: 12px;
		height: 12px;
		border-radius: 50%;
		background: var(--accent);
	}

	.timeline__label {
		font-size: 14px;
		font-weight: 500;
	}

	.timeline__date {
		font-size: 12px;
		color: var(--muted);
		margin-left: auto;
		white-space: nowrap;
	}

	.review {
		margin-top: 28px;
		background: var(--surface);
		border: 1px solid var(--border);
		border-radius: 16px;
		padding: 26px;
	}

	.review__title {
		font-size: 18px;
		font-weight: 600;
	}

	.review__link {
		display: inline-block;
		margin-top: 10px;
		font-size: 14px;
		font-weight: 600;
		color: var(--accent);
	}

	.review__link:hover {
		text-decoration: underline;
		text-underline-offset: 3px;
	}

	.review__done {
		margin-top: 12px;
		font-size: 14px;
		color: var(--muted);
	}

	@media (max-width: 900px) {
		.grid {
			grid-template-columns: 1fr;
		}
	}
</style>
