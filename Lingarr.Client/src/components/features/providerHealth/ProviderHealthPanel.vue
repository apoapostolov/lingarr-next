<template>
    <CardComponent title="Provider Health">
        <template #description>
            Provider configuration and recent translation status.
        </template>
        <template #content>
            <div v-if="loading" class="space-y-2" aria-label="Loading provider health">
                <div
                    v-for="index in 4"
                    :key="index"
                    class="bg-primary/70 h-14 animate-pulse rounded-md"></div>
            </div>

            <div v-else-if="loadError" class="space-y-3" role="alert">
                <p class="text-sm text-red-400">{{ loadError }}</p>
                <ButtonComponent variant="secondary" size="xs" @click="load">
                    Try again
                </ButtonComponent>
            </div>

            <div v-else class="space-y-2">
                <div
                    v-for="provider in providers"
                    :key="provider.provider"
                    class="border-accent/20 bg-primary/45 overflow-hidden rounded-md border">
                    <button
                        type="button"
                        class="group focus-visible:ring-accent flex min-h-14 w-full items-center gap-3 px-3 py-2 text-left focus-visible:ring-2 focus-visible:outline-none focus-visible:ring-inset"
                        :aria-expanded="expandedProvider === provider.provider"
                        @click="toggle(provider.provider)">
                        <span
                            class="h-3 w-3 shrink-0 rounded-full border-2"
                            :class="dotClass(provider.state)"
                            aria-hidden="true"></span>
                        <span class="min-w-0 grow">
                            <span class="text-primary-content block font-semibold">
                                {{ provider.displayName }}
                            </span>
                            <span class="text-secondary-content/70 block truncate text-xs">
                                {{ provider.model || lastRelevantEvent(provider) }}
                            </span>
                        </span>
                        <span
                            class="text-primary-content flex min-h-8 max-w-28 shrink-0 items-center px-2 text-right text-sm leading-tight font-medium">
                            {{ provider.statusLabel }}
                        </span>
                        <span
                            class="text-secondary-content/70 group-hover:border-accent/30 group-hover:bg-accent/10 flex h-8 w-8 shrink-0 items-center justify-center rounded-sm border border-transparent text-xl leading-none font-medium transition-colors"
                            aria-hidden="true">
                            {{ expandedProvider === provider.provider ? '−' : '+' }}
                        </span>
                    </button>

                    <div
                        v-if="expandedProvider === provider.provider"
                        class="border-accent/15 space-y-3 border-t px-3 py-3">
                        <p
                            v-if="provider.state !== 'healthy'"
                            class="text-secondary-content text-sm">
                            {{ provider.reason }}
                        </p>

                        <dl class="grid grid-cols-1 gap-2 text-xs sm:grid-cols-2">
                            <div>
                                <dt class="text-secondary-content/60">Configuration</dt>
                                <dd class="text-primary-content">
                                    {{
                                        provider.configured
                                            ? 'Complete'
                                            : `Missing: ${provider.missingFields.join(', ')}`
                                    }}
                                </dd>
                            </div>
                            <div>
                                <dt class="text-secondary-content/60">Recent success rate</dt>
                                <dd class="text-primary-content">
                                    {{
                                        provider.successRate == null
                                            ? 'No attempts'
                                            : `${provider.successRate}%`
                                    }}
                                </dd>
                            </div>
                            <div>
                                <dt class="text-secondary-content/60">Last success</dt>
                                <dd class="text-primary-content">
                                    {{ relativeTime(provider.lastSuccessAt) }}
                                </dd>
                            </div>
                            <div>
                                <dt class="text-secondary-content/60">Median response</dt>
                                <dd class="text-primary-content">
                                    {{
                                        provider.medianDurationMs == null
                                            ? 'Not available'
                                            : `${provider.medianDurationMs} ms`
                                    }}
                                </dd>
                            </div>
                        </dl>

                        <div class="flex flex-wrap items-center gap-x-3 gap-y-2">
                            <ButtonComponent
                                variant="secondary"
                                size="xs"
                                :disabled="!provider.configured"
                                :loading="testingProvider === provider.provider"
                                @click.stop="testProvider(provider.provider)">
                                Test provider
                            </ButtonComponent>
                            <p
                                v-if="probeResults[provider.provider]"
                                class="min-w-48 flex-1 text-sm"
                                :class="
                                    probeResults[provider.provider]?.success
                                        ? 'text-green-400'
                                        : 'text-yellow-400'
                                "
                                role="status">
                                {{ probeResults[provider.provider]?.message }}
                            </p>
                            <a
                                href="#"
                                class="text-accent-content focus-visible:ring-accent rounded-sm text-xs underline underline-offset-2 focus-visible:ring-2 focus-visible:outline-none sm:ml-auto"
                                @click.prevent="openSetup(provider.provider)">
                                Open Translation Setup
                            </a>
                        </div>
                    </div>
                </div>
            </div>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { nextTick, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { IProviderHealth, IProviderProbe, ProviderHealthState } from '@/ts'
import services from '@/services'
import ButtonComponent from '@/components/common/ButtonComponent.vue'
import CardComponent from '@/components/common/CardComponent.vue'

const route = useRoute()
const router = useRouter()
const providers = ref<IProviderHealth[]>([])
const loading = ref(true)
const loadError = ref<string | null>(null)
const expandedProvider = ref<string | null>(null)
const testingProvider = ref<string | null>(null)
const probeResults = ref<Record<string, IProviderProbe>>({})

const load = async () => {
    loading.value = true
    loadError.value = null
    try {
        providers.value = await services.providerHealth.list()
    } catch {
        loadError.value = 'Provider health could not be loaded.'
    } finally {
        loading.value = false
    }
}

const openSetup = async (provider: string) => {
    const hash = `#service-${provider}`
    if (route.name !== 'translation-setup-settings' || route.hash !== hash) {
        await router.push({ name: 'translation-setup-settings', hash })
    }
    await nextTick()
    document.getElementById(`service-${provider}`)?.scrollIntoView({
        behavior: 'smooth',
        block: 'center'
    })
}

const toggle = (provider: string) => {
    expandedProvider.value = expandedProvider.value === provider ? null : provider
}

const testProvider = async (provider: string) => {
    testingProvider.value = provider
    try {
        probeResults.value[provider] = await services.providerHealth.test(provider)
        providers.value = await services.providerHealth.list()
    } catch {
        probeResults.value[provider] = {
            provider,
            sourceLanguage: '',
            targetLanguage: '',
            supported: false,
            success: false,
            durationMs: 0,
            message: 'The provider test could not be completed.'
        }
    } finally {
        testingProvider.value = null
    }
}

const dotClass = (state: ProviderHealthState): string =>
    ({
        not_configured: 'border-gray-700 bg-gray-700',
        not_checked: 'border-gray-400 bg-transparent',
        healthy: 'border-green-500 bg-green-500',
        needs_attention: 'border-yellow-400 bg-yellow-400',
        recently_unavailable: 'border-red-300 bg-red-300',
        unavailable: 'border-red-600 bg-red-600'
    })[state]

const relativeTime = (value?: string | null): string => {
    if (!value) return 'Never'
    const elapsedSeconds = Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 1000))
    if (elapsedSeconds < 60) return 'Just now'
    const minutes = Math.floor(elapsedSeconds / 60)
    if (minutes < 60) return `${minutes} minute${minutes === 1 ? '' : 's'} ago`
    const hours = Math.floor(minutes / 60)
    if (hours < 48) return `${hours} hour${hours === 1 ? '' : 's'} ago`
    const days = Math.floor(hours / 24)
    return `${days} day${days === 1 ? '' : 's'} ago`
}

const lastRelevantEvent = (provider: IProviderHealth): string => {
    if (provider.lastSuccessAt) return `Last success ${relativeTime(provider.lastSuccessAt)}`
    if (provider.lastFailureAt) return `Last failure ${relativeTime(provider.lastFailureAt)}`
    return provider.reason
}

onMounted(load)
</script>
