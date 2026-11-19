<script lang="ts">
	import Eyebrow from '#lib/components/Eyebrow.svelte';
	import Button from '#lib/components/Button.svelte';
	import { workshop, workshopStats, masterProfile, aboutText, steps, reviews, getWorkshopStatus } from '../../mocks/db.ts';
	import { yandexWidgets } from '#config/yandex-widgets.ts';
	import { isMockMode } from '../../services/api.ts';

	const workshopStatus = getWorkshopStatus();
	const isOpen = workshopStatus.isOpen;
	const openStatus = workshopStatus.message;

	// Sprint-2: the backend dropped client reviews in favor of Yandex Maps,
	// so the showcase is the curated mockup set plus a link to the map listing.
	const showcase = reviews.map((review) => ({
		id: review.id,
		author: review.author,
		rating: 5,
		text: review.text,
		date: review.date
	}));
	let expanded = $state<string | null>(null);

	function stars(rating: number): string {
		return '★'.repeat(rating);
	}
</script>

<!-- ================= Hero ================= -->
<section class="hero">
	<div class="container hero__inner">
		<div class="hero__content">
			<Eyebrow dot tone="muted">Ремонт телефонов — мастерская рядом</Eyebrow>
			<h1 class="hero__title">Телефон снова в деле.<br /><span class="hero__title-accent">Вы — на связи.</span></h1>
			<p class="hero__subtitle">
				Сломался телефон? Оставьте заявку без звонка. Узнайте цену заранее и отслеживайте ремонт онлайн.
			</p>
			<div class="hero__actions">
				<Button href="/request" size="lg">Оставить заявку ↗</Button>
				<Button href="/price" variant="secondary" size="lg">Посмотреть цены</Button>
			</div>
		</div>
		<div class="hero__photo">
			<img src="/hero.jpg" alt="Мастер ремонтирует телефон в мастерской" />
		</div>
	</div>

	<div class="container hero__location">
		<div class="strip-card">
			<div class="location">
				<span class="location__pin" aria-hidden="true">
					<svg width="18" height="18" viewBox="0 0 24 24" fill="none">
						<path
							d="M12 21s-7-5.1-7-11a7 7 0 1 1 14 0c0 5.9-7 11-7 11Z"
							stroke="currentColor"
							stroke-width="2"
						/>
						<circle cx="12" cy="10" r="2.5" stroke="currentColor" stroke-width="2" />
					</svg>
				</span>
				<div class="location__info">
					<strong>{workshop.address}</strong>
					<span class="location__metro">{workshop.metroDistance}</span>
				</div>
			</div>
			<div class="location">
				<span class="location__pin" aria-hidden="true">
					<svg width="18" height="18" viewBox="0 0 24 24" fill="none">
						<circle cx="12" cy="12" r="9" stroke="currentColor" stroke-width="2" />
						<path d="M12 7v5l3 3" stroke="currentColor" stroke-width="2" stroke-linecap="round" />
					</svg>
				</span>
				<div class="location__info">
					<strong>{workshop.hours}</strong>
					<span class="location__status" class:location__status--open={isOpen} class:location__status--closed={!isOpen}>
						{openStatus}
					</span>
				</div>
			</div>
			<a class="location__link" href="https://yandex.ru/maps/?text={encodeURIComponent(workshop.address)}" target="_blank" rel="noreferrer">
				Как нас найти ↗
			</a>
		</div>
	</div>
</section>

<!-- ================= About ================= -->
<section class="section about">
	<div class="container about__inner">
		<div class="about__text">
			<Eyebrow>О мастерской</Eyebrow>
			<h2 class="h2">Не просто техника.<br />Ваши планы, фото и связь.</h2>
			<p class="about__paragraph">{aboutText}</p>
		</div>
		<div class="about__side">
			<dl class="about__stats">
				<div class="stat">
					<dd class="stat__value">{workshopStats.years}</dd>
					<dt class="stat__label">{workshopStats.yearsLabel}</dt>
				</div>
				<div class="stat">
					<dd class="stat__value">{workshopStats.saved}</dd>
					<dt class="stat__label">{workshopStats.savedLabel}</dt>
				</div>
				<div class="stat">
					<dd class="stat__value">{yandexWidgets.rating}</dd>
					<dt class="stat__label">оценка на Яндекс</dt>
				</div>
			</dl>
			<div class="about__master">
				<span class="about__master-avatar" aria-hidden="true">{masterProfile.initials}</span>
				<div class="about__master-info">
					<p class="about__master-name">{masterProfile.name}</p>
					<p class="about__master-bio">{masterProfile.role}. {masterProfile.promise}</p>
				</div>
			</div>
		</div>
	</div>
</section>

<!-- ================= Steps ================= -->
<section class="section steps-section">
	<div class="container">
		<div class="steps-head">
			<h2 class="h2">Как это работает</h2>
			<p class="steps-head__note">Четыре шага — и снова на связи</p>
		</div>
		<ol class="steps">
			{#each steps as step (step.num)}
				<li class="step">
					<div class="step__top">
						<span class="step__num">{step.num}</span>
						<!-- eslint-disable-next-line svelte/no-at-html-tags -- static inline SVG constants from src/mocks/db.ts, not user input -->
						<span class="step__icon" aria-hidden="true">{@html step.icon}</span>
					</div>
					<h3 class="step__title">{step.title}</h3>
					<p class="step__text">{step.text}</p>
				</li>
			{/each}
		</ol>
	</div>
</section>

<!-- ================= Reviews & map (card 12: real Yandex data) ================= -->
<section class="section reviews-section">
	<div class="container">
		<div class="reviews-head">
			<h2 class="h2">Нас советуют своим</h2>
			<p class="reviews-head__rating">
				<span class="reviews-head__stars" aria-hidden="true">★</span>
				<a
					class="reviews-head__link"
					href={yandexWidgets.reviewsUrl}
					target="_blank"
					rel="noreferrer"
				>
					{yandexWidgets.rating} · {yandexWidgets.ratingsCount} оценок на Яндекс ↗
				</a>
			</p>
		</div>

		{#if isMockMode}
			<!-- Демо без API: детерминированные отзывы остаются как страховка показа -->
			<div class="reviews">
				{#each showcase as review (review.id)}
					<article class="review">
						<p class="review__stars" aria-label={`Оценка ${review.rating} из 5`}>{stars(review.rating)}</p>
						<p class="review__text" class:review__text--clamped={expanded !== review.id && review.text.length > 140}>
							{review.text}
						</p>
						{#if review.text.length > 140 && expanded !== review.id}
							<button class="review__more" type="button" onclick={() => (expanded = review.id)}>читать полностью</button>
						{/if}
						<footer class="review__footer">
							<span class="review__avatar" aria-hidden="true">{review.author.slice(0, 1)}{review.author.split(' ')[1]?.[0] ?? ''}</span>
							<div class="review__meta">
								<span class="review__author">{review.author}</span>
								<span class="review__date">{review.date}</span>
							</div>
						</footer>
					</article>
				{/each}
			</div>
		{:else}
			<!-- Реальные данные организации: рейтинг-плитка + публичные ссылки -->
			<div class="reviews-live">
				<a
					class="reviews-live__card"
					href={yandexWidgets.reviewsUrl}
					target="_blank"
					rel="noreferrer"
				>
					<span class="reviews-live__rating">{yandexWidgets.rating}</span>
					<span class="reviews-live__stars" aria-hidden="true">★★★★☆</span>
					<span class="reviews-live__count">{yandexWidgets.ratingsCount} оценок на Яндекс.Картах</span>
					<span class="reviews-live__cta">Читать и оставить отзыв ↗</span>
				</a>

				<div class="reviews-live__map">
					<iframe
						class="reviews-live__iframe"
						src={yandexWidgets.mapUrl}
						title="Карта: мастерская «про телефон», Самара, проспект Ленина, 12А"
						loading="lazy"
						allowfullscreen
					></iframe>
					<p class="reviews-live__fallback">
						Карта не загрузилась? <a href={yandexWidgets.reviewsUrl} target="_blank" rel="noreferrer">Открыть организацию на Яндексе</a>.
					</p>
				</div>
			</div>
		{/if}
	</div>
</section>

<!-- ================= CTA ================= -->
<section class="section cta-section">
	<div class="container cta-section__inner">
		<div class="cta-section__copy">
			<h2 class="cta-section__title">Починим телефон. Освободим ваш день.</h2>
			<p class="cta-section__text">Оставьте заявку — всё остальное возьмём на себя.</p>
		</div>
		<Button href="/request" size="lg">Оставить заявку ↗</Button>
	</div>
</section>

<style>
	/* ---------- Hero ---------- */
	.hero {
		background: var(--bg);
		padding-top: 48px;
	}

	.hero__inner {
		display: grid;
		grid-template-columns: 1fr 1.05fr;
		gap: 48px;
		align-items: center;
	}

	.hero__title {
		font-size: clamp(34px, 4.6vw, 62px);
		font-weight: 800;
		line-height: 1.12;
		letter-spacing: -0.03em;
		margin: 16px 0 20px;
		max-width: 620px;
	}

	.hero__title-accent {
		color: var(--accent);
	}

	.hero__subtitle {
		font-size: 17px;
		line-height: 1.55;
		color: var(--muted);
		max-width: 440px;
	}

	.hero__actions {
		display: flex;
		gap: 12px;
		margin-top: 28px;
	}


	.hero__photo {
		position: relative;
	}

	.hero__photo img {
		width: 100%;
		height: 100%;
		object-fit: cover;
		border-radius: 20px;
	}


	.hero__location {
		display: grid;
		grid-template-columns: 1fr 1fr auto;
		gap: 32px;
		align-items: center;
		margin-top: 40px;
		padding-block: 0 48px;
	}

	.strip-card {
		grid-column: 1 / -1;
		background: var(--surface);
		border: 1px solid var(--border);
		border-radius: 16px;
		padding: 20px 28px;
		display: grid;
		grid-template-columns: 1fr 1fr auto;
		gap: 24px;
		align-items: center;
	}

	.location {
		display: flex;
		align-items: center;
		gap: 10px;
	}

	.location__pin {
		color: var(--accent);
		display: inline-flex;
	}

	.location__info {
		display: flex;
		flex-direction: column;
		line-height: 1.3;
	}

	.location__info strong {
		font-size: 15px;
		font-weight: 500;
	}

	.location__metro {
		font-size: 13px;
		color: var(--muted);
	}

	.location__status {
		font-size: 13px;
		color: var(--muted);
	}

	.location__status--open {
		color: var(--success);
	}

	.location__status--closed {
		color: var(--warning);
	}

	.location__link {
		font-size: 14px;
		font-weight: 500;
		color: var(--accent);
	}

	/* ---------- About ---------- */
	.about__inner {
		display: grid;
		grid-template-columns: 1.15fr 1fr;
		gap: 64px;
		align-items: center;
	}

	.about__paragraph {
		margin-top: 20px;
		font-size: 15px;
		line-height: 1.7;
		color: var(--muted);
		max-width: 520px;
	}

	.about__stats {
		display: grid;
		grid-template-columns: repeat(3, 1fr);
		gap: 24px;
		border-bottom: 1px solid var(--border);
		padding-bottom: 28px;
	}

	.stat__value {
		font-size: 34px;
		font-weight: 700;
		letter-spacing: -0.02em;
		margin: 0;
		white-space: nowrap;
	}

	.stat__label {
		font-size: 13px;
		color: var(--muted);
		margin-top: 6px;
	}

	.about__master {
		display: flex;
		align-items: center;
		gap: 16px;
		padding-top: 28px;
	}

	.about__master-avatar {
		width: 48px;
		height: 48px;
		border-radius: 50%;
		background: var(--accent);
		color: #ffffff;
		display: inline-flex;
		align-items: center;
		justify-content: center;
		font-size: 17px;
		font-weight: 600;
		flex-shrink: 0;
	}

	.about__master-name {
		font-size: 16px;
		font-weight: 600;
	}

	.about__master-bio {
		font-size: 14px;
		color: var(--muted);
		margin-top: 4px;
	}

	/* ---------- Steps ---------- */
	.steps-section {
		background: var(--bg-muted, var(--bg));
	}

	.steps-head {
		display: flex;
		align-items: baseline;
		justify-content: space-between;
		gap: 24px;
		margin-bottom: 36px;
	}

	.steps-head__note {
		font-size: 14px;
		color: var(--muted);
	}

	.steps {
		display: grid;
		grid-template-columns: repeat(4, 1fr);
		gap: 24px;
	}

	.step {
		background: var(--surface);
		border: 1px solid var(--border);
		border-radius: 16px;
		padding: 24px;
	}

	.step__top {
		display: flex;
		align-items: center;
		justify-content: space-between;
	}

	.step__num {
		font-size: 15px;
		font-weight: 600;
		color: var(--accent);
	}

	.step__icon {
		color: var(--accent);
		display: inline-flex;
	}

	.step__title {
		font-size: 18px;
		font-weight: 600;
		margin-top: 16px;
	}

	.step__text {
		font-size: 14px;
		line-height: 1.6;
		color: var(--muted);
		margin-top: 10px;
	}

	/* ---------- Reviews (card 12: real Yandex) ---------- */
	.reviews-live {
		display: grid;
		grid-template-columns: minmax(0, 0.55fr) minmax(0, 1.45fr);
		gap: 24px;
		align-items: start;
	}

	.reviews-live__card {
		display: flex;
		flex-direction: column;
		gap: 8px;
		background: var(--surface);
		border: 1px solid var(--border);
		border-radius: 20px;
		padding: 24px 26px;
		text-decoration: none;
		transition: border-color 0.15s ease, transform 0.15s ease;
	}

	.reviews-live__card:hover {
		border-color: var(--accent);
		transform: translateY(-2px);
	}

	.reviews-live__rating {
		font-size: 32px;
		font-weight: 800;
		letter-spacing: -0.02em;
		line-height: 1;
	}

	.reviews-live__stars {
		color: var(--warning);
		letter-spacing: 3px;
	}

	.reviews-live__count {
		color: var(--muted);
		font-size: 14px;
	}

	.reviews-live__cta {
		margin-top: 10px;
		font-size: 14px;
		font-weight: 600;
		color: var(--accent);
	}

	.reviews-live__map {
		display: flex;
		flex-direction: column;
		gap: 10px;
	}

	.reviews-live__iframe {
		width: 100%;
		min-height: 320px;
		border: 0;
		border-radius: 20px;
	}

	.reviews-live__fallback {
		font-size: 12px;
		color: var(--muted);
	}

	.reviews-live__fallback a {
		color: var(--accent);
	}

	/* ---------- Reviews ---------- */
	.reviews-section {
		background: var(--bg-muted, var(--bg));
	}

	.reviews-head {
		display: flex;
		align-items: baseline;
		justify-content: space-between;
		gap: 24px;
		margin-bottom: 32px;
	}

	.reviews-head__rating {
		font-size: 14px;
		font-weight: 500;
		color: var(--accent);
	}

	.reviews-head__stars {
		color: var(--accent);
	}

	.reviews-head__link {
		font-size: 14px;
		font-weight: 500;
		color: var(--accent);
	}

	.reviews-head__link:hover {
		text-decoration: underline;
		text-underline-offset: 3px;
	}

	.reviews {
		display: grid;
		grid-template-columns: repeat(3, 1fr);
		gap: 24px;
	}

	.review {
		background: var(--surface);
		border: 1px solid var(--border);
		border-radius: 16px;
		padding: 24px;
		display: flex;
		flex-direction: column;
		gap: 14px;
	}

	.review__stars {
		color: #d97706;
		letter-spacing: 3px;
		font-size: 15px;
		margin: 0;
	}

	.review__text {
		font-size: 14px;
		line-height: 1.65;
	}

	.review__text--clamped {
		display: -webkit-box;
		-webkit-line-clamp: 3;
		line-clamp: 3;
		-webkit-box-orient: vertical;
		overflow: hidden;
	}

	.review__more {
		align-self: flex-start;
		font-size: 12px;
		font-weight: 500;
		color: var(--accent);
		margin-top: -6px;
	}

	.review__more:hover {
		text-decoration: underline;
		text-underline-offset: 3px;
	}

	.review__footer {
		margin-top: auto;
		display: flex;
		align-items: center;
		gap: 12px;
	}

	.review__avatar {
		width: 40px;
		height: 40px;
		border-radius: 50%;
		background: var(--bg-muted, var(--bg));
		color: var(--muted);
		display: inline-flex;
		align-items: center;
		justify-content: center;
		font-size: 12px;
		font-weight: 600;
	}

	.review__meta {
		display: flex;
		flex-direction: column;
	}

	.review__author {
		font-size: 13px;
		font-weight: 500;
	}

	.review__date {
		font-size: 11px;
		color: var(--muted);
	}

	/* ---------- CTA ---------- */
	.cta-section {
		padding-block: 40px 72px;
	}

	.cta-section__inner {
		background: var(--accent);
		color: #ffffff;
		border-radius: 24px;
		padding: 48px 56px;
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 32px;
	}

	.cta-section__title {
		font-size: clamp(26px, 3vw, 38px);
		font-weight: 700;
		letter-spacing: -0.02em;
	}

	.cta-section__text {
		font-size: 15px;
		opacity: 0.85;
		margin-top: 10px;
	}

	.cta-section :global(.btn--primary) {
		background: #ffffff;
		color: var(--accent);
		flex-shrink: 0;
	}

	.cta-section :global(.btn--primary:hover) {
		background: var(--bg);
	}

	/* ---------- Responsive ---------- */
	@media (max-width: 1024px) {
		.steps {
			grid-template-columns: repeat(2, 1fr);
		}

		.reviews {
			grid-template-columns: 1fr;
		}

		.about__inner,
		.hero__inner {
			grid-template-columns: 1fr;
		}

		.hero__photo {
			display: none;
		}

		.strip-card {
			grid-template-columns: 1fr;
			gap: 18px;
		}

		.steps-head,
		.reviews-head {
			flex-direction: column;
			align-items: flex-start;
			gap: 8px;
		}
	}

	@media (max-width: 480px) {
		.hero {
			padding-top: 24px;
		}

		.hero__actions {
			flex-direction: column;
			width: 100%;
		}

		.steps {
			grid-template-columns: 1fr;
		}

		.cta-section__inner {
			padding: 32px 24px;
			flex-direction: column;
			align-items: flex-start;
		}

		.hero__title br {
			display: none;
		}
	}
</style>
