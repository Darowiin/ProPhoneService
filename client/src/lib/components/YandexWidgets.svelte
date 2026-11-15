<script lang="ts">
	import type { Snippet } from 'svelte';
	/** Sprint 3, card 12: клиентская интеграция JS-виджетов Яндекса.
	 *  Никакого API на беке — скрипты грузит браузер; повторная навигация
	 *  не создаёт дублей: контейнер и скрипт чистятся при уничтожении. */
	import { onMount } from 'svelte';
	import { yandexWidgets } from '../../config/yandex-widgets.ts';

	interface Props {
		/** Fallback-блок (сниппет родителя), показывается пока виджеты не включены. */
		children: Snippet;
	}

	let { children }: Props = $props();

	type Phase = 'disabled' | 'loading' | 'ready' | 'error';

	let reviewsPhase = $state<Phase>(yandexWidgets.enabled ? 'loading' : 'disabled');
	let mapPhase = $state<Phase>(yandexWidgets.enabled ? 'loading' : 'disabled');

	/** Загружает скрипт один раз и гарантирует удаление при разборке. */
	function loadWidget(
		src: string,
		containerId: string,
		setPhase: (phase: Phase) => void
	): () => void {
		if (!src) {
			setPhase('error');
			return () => {};
		}
		const script = document.createElement('script');
		script.src = src;
		script.async = true;
		script.onload = () => setPhase('ready');
		script.onerror = () => setPhase('error');
		document.body.appendChild(script);

		return () => {
			script.remove();
			const container = document.getElementById(containerId);
			if (container) container.innerHTML = '';
		};
	}

	onMount(() => {
		if (!yandexWidgets.enabled) return;
		const unloadReviews = loadWidget(
			yandexWidgets.reviewsScriptUrl,
			yandexWidgets.reviewsContainerId,
			(phase) => (reviewsPhase = phase)
		);
		const unloadMap = loadWidget(
			yandexWidgets.mapScriptUrl,
			yandexWidgets.mapContainerId,
			(phase) => (mapPhase = phase)
		);
		return () => {
			unloadReviews();
			unloadMap();
		};
	});
</script>

{#if yandexWidgets.enabled}
	<div class="widget-block">
		<div class="widget-block__head">
			<h2 class="h2">Нас советуют своим</h2>
			<a class="widget-block__link" href={yandexWidgets.fallbackUrl} target="_blank" rel="noreferrer">
				Отзывы и карта на Яндексе ↗
			</a>
		</div>

		<div class="widget-slot" class:widget-slot--pending={reviewsPhase === 'loading'}>
			{#if reviewsPhase === 'loading'}
				<p class="widget-slot__hint">Загружаем отзывы из Яндекса…</p>
			{:else if reviewsPhase === 'error'}
				<p class="widget-slot__hint">
					Виджет отзывов сейчас недоступен. Смотреть и читать отзывы можно
					<a href={yandexWidgets.fallbackUrl} target="_blank" rel="noreferrer">на Яндекс.Картах</a>.
				</p>
			{/if}
			<div id={yandexWidgets.reviewsContainerId}></div>
		</div>

		<div class="widget-slot widget-slot--map" class:widget-slot--pending={mapPhase === 'loading'}>
			{#if mapPhase === 'loading'}
				<p class="widget-slot__hint">Загружаем карту…</p>
			{:else if mapPhase === 'error'}
				<p class="widget-slot__hint">
					Карта временно недоступна — <a href={yandexWidgets.fallbackUrl} target="_blank" rel="noreferrer">открыть маршрут на Яндексе</a>.
				</p>
			{/if}
			<div id={yandexWidgets.mapContainerId}></div>
		</div>
	</div>
{:else}
	<!-- Материалы PO ещё не получены: виджеты не грузим, показываем fallback-блок родителя. -->
	{@render children()}
{/if}

<style>
	.widget-block__head {
		display: flex;
		justify-content: space-between;
		align-items: baseline;
		gap: 16px;
		margin-bottom: 20px;
	}

	.widget-block__link {
		font-size: 13px;
		color: var(--accent);
		white-space: nowrap;
	}

	.widget-slot {
		min-height: 120px;
		border: 1px dashed var(--border);
		border-radius: 16px;
		padding: 16px;
		margin-bottom: 20px;
	}

	.widget-slot--map {
		min-height: 320px;
	}

	.widget-slot--pending {
		display: grid;
		place-items: center;
	}

	.widget-slot__hint {
		color: var(--muted);
		font-size: 14px;
	}
</style>
