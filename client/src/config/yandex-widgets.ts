/** Sprint 3, card 12: параметры JS-виджетов Яндекса.
 *
 *  PO передаёт ссылку/идентификатор организации и материалы подключения
 *  (карточка 20). Пока материалы не согласованы, `enabled = false` — на
 *  сайте показывается прежний блок отзывов, виджеты не грузятся.
 *  Секретов тут нет: только публичные параметры браузера. */
export interface YandexWidgetsConfig {
	/** Включать ли загрузку внешних скриптов виджетов. */
	enabled: boolean;
	/** Идентификатор организации в Яндекс.Картах (ссылка от PO). */
	orgId: string;
	/** Скрипт и контейнер виджета отзывов. */
	reviewsScriptUrl: string;
	reviewsContainerId: string;
	/** Скрипт и контейнер карты с меткой мастерской. */
	mapScriptUrl: string;
	mapContainerId: string;
	/** Резервная ссылка «смотреть на Яндексе» на случай недоступности. */
	fallbackUrl: string;
}

export const yandexWidgets: YandexWidgetsConfig = {
	enabled: false,
	orgId: '',
	reviewsScriptUrl: '',
	reviewsContainerId: 'yandex-reviews-widget',
	mapScriptUrl: '',
	mapContainerId: 'yandex-map-widget',
	fallbackUrl: 'https://yandex.ru/maps/org/pro_telefon/'
};
