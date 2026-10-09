<template>
    <div class="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <CardComponent title="Media Overview" class="lg:col-span-2">
            <template #description>
                Subtitle coverage in connected libraries for the selected language.
            </template>
            <template #actions>
                <div class="flex items-center gap-3">
                    <RefreshBadge
                        :refreshing="statisticsRefreshing"
                        :fetched-at="statisticsFetchedAt" />
                    <label class="flex items-center gap-2">
                        <span class="sr-only">Primary subtitle language</span>
                        <select
                            v-model="primaryLanguage"
                            aria-label="Primary subtitle language"
                            class="cursor-pointer rounded-md border-0 bg-transparent px-2 py-1 text-right text-sm font-semibold text-primary-content outline-none focus-visible:ring-2 focus-visible:ring-accent"
                            :disabled="languagesLoading"
                            @change="changePrimaryLanguage">
                            <option
                                v-for="language in languages"
                                :key="language.code"
                                :value="language.code"
                                class="bg-primary text-primary-content">
                                {{ language.name }}
                            </option>
                        </select>
                    </label>
                </div>
            </template>
            <template #content>
                <div v-if="loading" class="flex h-32 items-center justify-center">
                    <LoaderCircleIcon class="h-8 w-8 animate-spin" />
                </div>
                <div v-else-if="statistics" class="grid grid-cols-1 gap-4 md:grid-cols-2">
                    <StatCard
                        title="Movies"
                        :total="statistics.totalMovies"
                        :translated="getCoverageCount(MEDIA_TYPE.MOVIE)"
                        value-label="With subtitles" />
                    <StatCard
                        title="TV Episodes"
                        :total="statistics.totalEpisodes"
                        :translated="getCoverageCount(MEDIA_TYPE.EPISODE)"
                        value-label="With subtitles" />
                </div>
                <p v-else class="py-8 text-center text-sm text-primary-content/60">
                    Media statistics are unavailable.
                </p>

                <div
                    v-if="activityLoading"
                    class="mt-5 h-16 animate-pulse rounded-md bg-primary/45"></div>
                <p
                    v-else-if="activity"
                    class="mt-5 border-t border-accent/25 pt-5 text-lg leading-8 text-primary-content/80">
                    <template v-for="(fragment, index) in narrativeFragments" :key="index">
                        <strong
                            v-if="fragment.emphasized"
                            class="font-semibold text-primary-content tabular-nums">
                            {{ fragment.text }}
                        </strong>
                        <span v-else>{{ fragment.text }}</span>
                    </template>
                </p>
            </template>
        </CardComponent>

        <ProviderHealthPanel />

        <CardComponent title="Recent Activity">
            <template #description>
                Translation requests completed during the selected period.
            </template>
            <template #content>
                <div class="mb-4 flex items-center justify-between gap-3">
                    <div class="flex items-center gap-3">
                        <span class="text-xs font-semibold tracking-wide text-primary-content/50 uppercase">
                            Time window
                        </span>
                        <RefreshBadge
                            :refreshing="activityRefreshing"
                            :fetched-at="activityFetchedAt" />
                    </div>
                    <select
                        v-model.number="windowHours"
                        aria-label="Dashboard activity time window"
                        class="rounded-md border border-accent bg-secondary px-3 py-2 text-sm text-primary-content"
                        @change="changeWindow">
                        <option :value="12">Last 12 hours</option>
                        <option :value="24">Last 24 hours</option>
                        <option :value="48">Last 48 hours</option>
                        <option :value="72">Last 3 days</option>
                        <option :value="168">Last 7 days</option>
                    </select>
                </div>

                <div v-if="activityLoading" class="flex h-48 items-center justify-center">
                    <LoaderCircleIcon class="h-8 w-8 animate-spin" />
                </div>
                <template v-else-if="activity">
                    <div class="grid grid-cols-2 gap-3">
                        <MetricCard title="Subtitle files" :value="activity.completedFiles" />
                        <MetricCard title="Dialogue lines" :value="activity.translatedLines" />
                        <MetricCard title="Still running" :value="activity.activeTranslations" />
                        <MetricCard title="Recovered by fallback" :value="activity.fallbackRecoveries" />
                    </div>

                    <div class="mt-5">
                        <div class="mb-2 flex items-center justify-between gap-3">
                            <div class="flex items-center gap-3">
                                <h3 class="text-sm font-semibold text-primary-content">
                                    Daily translation history
                                </h3>
                                <RefreshBadge
                                    :refreshing="chartRefreshing"
                                    :fetched-at="chartFetchedAt" />
                            </div>
                            <span class="text-xs text-primary-content/50">Last 30 days</span>
                        </div>
                        <div class="h-80">
                            <div
                                v-if="chartLoading"
                                class="flex h-full items-center justify-center">
                                <LoaderCircleIcon class="h-8 w-8 animate-spin" />
                            </div>
                            <LanguageChart
                                v-else-if="dailyStats.length"
                                :daily-stats="dailyStats" />
                            <div
                                v-else
                                class="flex h-full items-center justify-center text-sm text-primary-content/60">
                                No daily translation history is available.
                            </div>
                        </div>
                    </div>

                    <div v-if="activity.topProviders.length" class="mt-4">
                        <h3 class="mb-2 text-xs font-semibold tracking-wide text-primary-content/50 uppercase">
                            Most used providers
                        </h3>
                        <div class="flex flex-wrap gap-2">
                            <span
                                v-for="provider in activity.topProviders"
                                :key="provider.name"
                                class="rounded-full border border-accent/35 bg-accent/10 px-2.5 py-1 text-xs text-primary-content">
                                {{ provider.name }} · {{ provider.count.toLocaleString() }} lines
                            </span>
                        </div>
                    </div>

                    <section class="mt-6 border-t border-accent/25 pt-5">
                        <div class="mb-4">
                            <h3 class="text-lg font-semibold text-primary-content">
                                Subtitle Quality
                            </h3>
                            <p class="mt-1 text-sm text-primary-content/55">
                                Rule-based assessment of completed subtitles during this period.
                            </p>
                        </div>
                        <div class="grid grid-cols-3 gap-3">
                            <MetricCard title="Checked" :value="activity.qualityChecked" />
                            <MetricCard title="Passed" :value="activity.qualityPassed" />
                            <MetricCard title="Needs review" :value="activity.qualityNeedsReview" />
                        </div>
                        <div class="mt-5 rounded-md border border-accent/25 bg-primary/45 p-4">
                            <div class="text-sm text-primary-content/60">
                                Average quality score
                            </div>
                            <div class="mt-1 text-3xl font-bold text-primary-content">
                                {{ activity.averageQualityScore ?? '—' }}
                                <span class="text-sm font-normal text-primary-content/45">
                                    / 100
                                </span>
                            </div>
                            <p class="mt-2 text-xs text-primary-content/50">
                                Scores reflect rule-based output checks, including formatting and consistency.
                            </p>
                        </div>
                        <div v-if="activity.topLanguagePairs.length" class="mt-4 space-y-2">
                            <div
                                v-for="pair in activity.topLanguagePairs"
                                :key="pair.name"
                                class="flex items-center justify-between rounded-md bg-primary/45 px-3 py-2 text-sm">
                                <span class="text-primary-content">{{ pair.name }}</span>
                                <span class="text-primary-content/55">
                                    {{ pair.count }}
                                    {{ pair.count === 1 ? 'file' : 'files' }}
                                </span>
                            </div>
                        </div>
                    </section>
                </template>
                <p v-else class="py-8 text-center text-sm text-primary-content/60">
                    Recent activity is unavailable.
                </p>
            </template>
        </CardComponent>

        <CardComponent title="All-time Totals" class="lg:col-span-2">
            <template #description>
                Lifetime totals for library and translation activity.
            </template>
            <template #content>
                <div class="grid grid-cols-1 gap-3 sm:grid-cols-3">
                    <MetricCard
                        title="Files processed"
                        :value="statistics?.totalFilesTranslated ?? 0" />
                    <MetricCard
                        title="Lines translated"
                        :value="statistics?.totalLinesTranslated ?? 0" />
                    <MetricCard
                        title="Characters translated"
                        :value="statistics?.totalCharactersTranslated ?? 0" />
                </div>
            </template>
        </CardComponent>
    </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
    DailyStatistic,
    IDashboardActivity,
    ILanguage,
    MEDIA_TYPE,
    SETTINGS,
    Statistics
} from '@/ts'
import services from '@/services'
import { useCachedResource } from '@/composables/useCachedResource'
import CardComponent from '@/components/common/CardComponent.vue'
import RefreshBadge from '@/components/common/RefreshBadge.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
import ProviderHealthPanel from '@/components/features/providerHealth/ProviderHealthPanel.vue'
import LanguageChart from './LanguageChart.vue'
import MetricCard from './MetricCard.vue'
import StatCard from './StatCard.vue'

// Stale-while-revalidate: each section hydrates from localStorage on setup so
// the dashboard renders last-known data instantly, then refreshes in the
// background. Numbers animate when fresh data lands (see AnimatedNumber).
const windowHours = ref(48)

const statisticsResource = useCachedResource<Statistics>(
    'dashboard:statistics',
    () => services.statistics.getStatistics<Statistics>()
)
const dailyResource = useCachedResource<DailyStatistic[]>(
    'dashboard:dailyStats',
    () => services.statistics.getDailyStatistics<DailyStatistic[]>(30)
)
const activityResource = useCachedResource<IDashboardActivity>(
    'dashboard:activity',
    () => services.dashboard.activity(windowHours.value)
)

// Sync the activity time window from cached/payload data so the select
// reflects what is actually being shown.
if (activityResource.data.value) {
    windowHours.value = activityResource.data.value.windowHours
}

const statistics = computed(() => statisticsResource.data.value)
const activity = computed(() => activityResource.data.value)
const dailyStats = computed(() => dailyResource.data.value ?? [])

// A section is "loading" only when we have nothing to render at all — neither
// cache nor a successful network response.
const loading = computed(() => statisticsResource.loading.value)
const activityLoading = computed(() => activityResource.loading.value)
const chartLoading = computed(() => dailyResource.loading.value)
const statisticsRefreshing = computed(() => statisticsResource.refreshing.value)
const activityRefreshing = computed(() => activityResource.refreshing.value)
const chartRefreshing = computed(() => dailyResource.refreshing.value)
const statisticsFetchedAt = computed(() => statisticsResource.fetchedAt.value)
const activityFetchedAt = computed(() => activityResource.fetchedAt.value)
const chartFetchedAt = computed(() => dailyResource.fetchedAt.value)

const languages = ref<ILanguage[]>([])
const languagesLoading = ref(true)
const primaryLanguage = ref('')

const getCoverageCount = (type: string): number => {
    if (!primaryLanguage.value) return 0
    const key = `coverage:${type}:${primaryLanguage.value.toLowerCase()}`
    return statistics.value?.subtitlesByLanguage?.[key] || 0
}

const narrativeFragments = computed(() => {
    if (!activity.value) return []

    const text = activity.value.narrative.join(' ')
    const namedValues = [
        ...activity.value.topProviders.map((provider) => provider.name),
        ...activity.value.topLanguagePairs.map((pair) => pair.name)
    ]
        .filter((value, index, values) => value && values.indexOf(value) === index)
        .sort((left, right) => right.length - left.length)
    const escapedValues = namedValues.map((value) =>
        value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    )
    const pattern = new RegExp(
        `(${['\\b\\d[\\d,.]*\\b', ...escapedValues].join('|')})`,
        'gi'
    )
    const emphasizedValues = new Set(namedValues.map((value) => value.toLocaleLowerCase()))

    return text
        .split(pattern)
        .filter(Boolean)
        .map((fragment) => ({
            text: fragment,
            emphasized:
                /^\d[\d,.]*$/.test(fragment) ||
                emphasizedValues.has(fragment.toLocaleLowerCase())
        }))
})

const fetchActivity = async (hours?: number) => {
    if (hours !== undefined) windowHours.value = hours
    await activityResource.refresh()
    const payload = activityResource.data.value
    if (payload) windowHours.value = payload.windowHours
}

const parseLanguages = (value: ILanguage[] | string): ILanguage[] => {
    if (Array.isArray(value)) return value
    try {
        const parsed = JSON.parse(value)
        return Array.isArray(parsed) ? parsed : []
    } catch {
        return []
    }
}

const initializePrimaryLanguage = async () => {
    languagesLoading.value = true
    try {
        const [available, saved, targets] = await Promise.all([
            services.translate.getLanguages<ILanguage[]>(),
            services.setting.getSetting<string>(SETTINGS.DASHBOARD_PRIMARY_LANGUAGE),
            services.setting.getSetting<ILanguage[] | string>(SETTINGS.TARGET_LANGUAGES)
        ])
        languages.value = [...available].sort((left, right) =>
            left.name.localeCompare(right.name)
        )

        const availableCodes = new Set(languages.value.map((language) => language.code))
        const configuredTarget = parseLanguages(targets)[0]?.code
        primaryLanguage.value =
            [saved, configuredTarget, 'en'].find(
                (code) => code && availableCodes.has(code)
            ) ?? languages.value[0]?.code ?? ''
    } finally {
        languagesLoading.value = false
    }
}

const changePrimaryLanguage = async () => {
    await services.setting.setSetting(
        SETTINGS.DASHBOARD_PRIMARY_LANGUAGE,
        primaryLanguage.value
    )
}

const changeWindow = async () => {
    await services.setting.setSetting(
        SETTINGS.DASHBOARD_ACTIVITY_WINDOW_HOURS,
        String(windowHours.value)
    )
    await fetchActivity(windowHours.value)
}

onMounted(() => {
    // Kick off background refreshes. Cached data is already shown; these
    // populate fresh values (animating the change) and update the cache.
    void initializePrimaryLanguage()
    void statisticsResource.refresh()
    void dailyResource.refresh()
    void fetchActivity()
})
</script>
