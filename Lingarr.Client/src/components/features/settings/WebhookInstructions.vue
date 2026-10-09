<template>
    <CardComponent title="Webhook">
        <template #description>
            Configure media server webhooks to queue new translations.
        </template>
        <template #content>
            <div class="flex flex-col space-y-2">
                <template v-for="provider in providers" :key="provider.name">
                    <span class="font-semibold">{{ provider.name }}</span>
                    <CodeSnippet class="mt-1 block">
                        <template v-for="(part, index) in wrapUrl(provider.url)" :key="index">
                            <wbr v-if="index > 0" />{{ part }}
                        </template>
                    </CodeSnippet>
                </template>
            </div>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import CardComponent from '@/components/common/CardComponent.vue'
import CodeSnippet from '@/components/common/CodeSnippet.vue'
import { resolveUrl } from '@/utils/baseUrl'

const providers = [
    { name: 'Radarr', url: resolveUrl('/api/webhook/radarr') },
    { name: 'Sonarr', url: resolveUrl('/api/webhook/sonarr') },
    { name: 'Plex', url: resolveUrl('/api/webhook/plex') },
    { name: 'Jellyfin', url: resolveUrl('/api/webhook/jellyfin') },
    { name: 'Emby', url: resolveUrl('/api/webhook/emby') }
]

function wrapUrl(url: string): string[] {
    const scheme = url.indexOf('://')
    const pathStart = scheme < 0 ? -1 : url.indexOf('/', scheme + 3)
    if (pathStart < 0) {
        return [url]
    }

    const segments = url
        .slice(pathStart)
        .split('/')
        .filter((segment) => segment.length > 0)
        .map((segment) => `/${segment}`)
    return [url.slice(0, pathStart), ...segments]
}
</script>
