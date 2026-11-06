// SvelteKit 3: `$app/environment` re-exports `$app/env` at runtime, but this
// Kit version ships no ambient type declaration for it — the rest of the
// `$app/*` modules live in @sveltejs/kit/types. Keep the checker green by
// mirroring the env surface here.
declare module '$app/environment' {
	export { browser, dev, building, version } from '$app/env';
}
