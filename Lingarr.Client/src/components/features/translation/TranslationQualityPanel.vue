<template>
    <CardComponent title="Subtitle Quality">
        <template #description>
            Rule-based checks flag subtitle issues for review.
        </template>
        <template #content>
            <div v-if="loading" class="flex items-center gap-2 py-4 text-primary-content/60">
                <LoaderCircleIcon class="h-4 w-4 animate-spin" />
                Loading subtitle assessment…
            </div>

            <div v-else-if="quality?.summary" class="space-y-5">
                <div class="flex flex-wrap items-center justify-between gap-4">
                    <div class="flex items-center gap-4">
                        <div
                            class="flex h-20 w-20 shrink-0 flex-col items-center justify-center rounded-full border-4"
                            :class="scoreClasses">
                            <span class="text-2xl font-bold">{{ quality.summary.score ?? '—' }}</span>
                            <span class="text-[10px] font-semibold tracking-wide uppercase"
                                >out of 100</span
                            >
                        </div>
                        <div>
                            <div class="text-lg font-semibold text-primary-content">
                                {{ quality.summary.grade }}
                            </div>
                            <div class="mt-1 text-sm text-primary-content/60">
                                {{ quality.summary.lineCount }} lines checked ·
                                {{ formatDateTime(quality.summary.evaluatedAt) }}
                            </div>
                        </div>
                    </div>
                    <ButtonComponent
                        variant="secondary"
                        size="sm"
                        :loading="reEvaluating"
                        @click="reEvaluate">
                        Re-evaluate
                    </ButtonComponent>
                </div>

                <div class="grid grid-cols-3 gap-2">
                    <div class="rounded-md border border-red-500/40 bg-red-500/10 p-3">
                        <div class="text-xl font-bold text-red-300">
                            {{ quality.summary.criticalCount }}
                        </div>
                        <div class="text-xs text-primary-content/60">Critical</div>
                    </div>
                    <div class="rounded-md border border-orange-500/40 bg-orange-500/10 p-3">
                        <div class="text-xl font-bold text-orange-300">
                            {{ quality.summary.errorCount }}
                        </div>
                        <div class="text-xs text-primary-content/60">Errors</div>
                    </div>
                    <div class="rounded-md border border-yellow-500/40 bg-yellow-500/10 p-3">
                        <div class="text-xl font-bold text-yellow-200">
                            {{ quality.summary.warningCount }}
                        </div>
                        <div class="text-xs text-primary-content/60">Warnings</div>
                    </div>
                </div>

                <div v-if="quality.findings.length > 0" class="space-y-3">
                    <div class="flex flex-wrap gap-2">
                        <select
                            v-model="severityFilter"
                            aria-label="Filter findings by severity"
                            class="rounded-md border border-accent bg-secondary px-3 py-2 text-sm text-primary-content">
                            <option value="">All severities</option>
                            <option value="critical">Critical</option>
                            <option value="error">Errors</option>
                            <option value="warning">Warnings</option>
                            <option value="info">Information</option>
                        </select>
                        <select
                            v-model="categoryFilter"
                            aria-label="Filter findings by category"
                            class="rounded-md border border-accent bg-secondary px-3 py-2 text-sm text-primary-content">
                            <option value="">All categories</option>
                            <option v-for="category in categories" :key="category" :value="category">
                                {{ category }}
                            </option>
                        </select>
                        <span class="self-center text-xs text-primary-content/50">
                            {{ filteredFindings.length }} findings shown
                        </span>
                    </div>

                    <div class="max-h-80 space-y-2 overflow-y-auto pr-1">
                        <button
                            v-for="finding in filteredFindings"
                            :key="finding.id"
                            type="button"
                            class="flex w-full items-start gap-3 rounded-md border p-3 text-left transition-colors"
                            :class="findingClasses(finding.severity)"
                            @click="
                                finding.linePosition !== null &&
                                emit('focus-line', finding.linePosition)
                            ">
                            <span
                                class="mt-1 h-2.5 w-2.5 shrink-0 rounded-full"
                                :class="dotClasses(finding.severity)"></span>
                            <span class="min-w-0">
                                <span class="block text-sm font-semibold text-primary-content">
                                    {{
                                        finding.linePosition === null
                                            ? 'Whole subtitle'
                                            : `Line ${finding.linePosition}`
                                    }}
                                    · {{ finding.category }}
                                </span>
                                <span class="mt-0.5 block text-sm text-primary-content/70">
                                    {{ finding.summary }}
                                </span>
                            </span>
                        </button>
                    </div>
                </div>

                <div
                    v-else
                    class="rounded-md border border-green-500/35 bg-green-500/10 p-4 text-sm text-green-200">
                    No quality findings for this subtitle.
                </div>
            </div>

            <div v-else class="flex flex-wrap items-center justify-between gap-3 py-2">
                <p class="text-sm text-primary-content/65">
                    No quality assessment is available.
                </p>
                <ButtonComponent
                    variant="secondary"
                    size="sm"
                    :loading="reEvaluating"
                    @click="reEvaluate">
                    Run assessment
                </ButtonComponent>
            </div>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import services from '@/services'
import { ITranslationQualityDetail, ITranslationQualityFinding } from '@/ts'
import { formatDateTime } from '@/utils/date'
import ButtonComponent from '@/components/common/ButtonComponent.vue'
import CardComponent from '@/components/common/CardComponent.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'

const props = defineProps<{ requestId: number }>()
const emit = defineEmits<{ 'focus-line': [position: number] }>()

const quality = ref<ITranslationQualityDetail | null>(null)
const loading = ref(true)
const reEvaluating = ref(false)
const severityFilter = ref('')
const categoryFilter = ref('')

const categories = computed(() =>
    [...new Set(quality.value?.findings.map((finding) => finding.category) ?? [])].sort()
)
const filteredFindings = computed(
    () =>
        quality.value?.findings.filter(
            (finding) =>
                (!severityFilter.value || finding.severity === severityFilter.value) &&
                (!categoryFilter.value || finding.category === categoryFilter.value)
        ) ?? []
)
const scoreClasses = computed(() => {
    const score = quality.value?.summary?.score ?? 0
    if (score >= 85) return 'border-green-500/70 bg-green-500/10 text-green-200'
    if (score >= 70) return 'border-yellow-500/70 bg-yellow-500/10 text-yellow-200'
    if (score >= 50) return 'border-orange-500/70 bg-orange-500/10 text-orange-200'
    return 'border-red-500/70 bg-red-500/10 text-red-200'
})

const load = async () => {
    try {
        quality.value = await services.translationRequest.quality(props.requestId)
    } catch {
        quality.value = null
    } finally {
        loading.value = false
    }
}

const reEvaluate = async () => {
    reEvaluating.value = true
    try {
        await services.translationRequest.reEvaluateQuality(props.requestId)
        await load()
    } finally {
        reEvaluating.value = false
    }
}

const findingClasses = (severity: ITranslationQualityFinding['severity']) => ({
    'border-red-500/40 bg-red-500/10 hover:bg-red-500/15': severity === 'critical',
    'border-orange-500/40 bg-orange-500/10 hover:bg-orange-500/15': severity === 'error',
    'border-yellow-500/35 bg-yellow-500/10 hover:bg-yellow-500/15': severity === 'warning',
    'border-accent/30 bg-secondary/40 hover:bg-accent/5': severity === 'info'
})
const dotClasses = (severity: ITranslationQualityFinding['severity']) => ({
    'bg-red-500': severity === 'critical',
    'bg-orange-400': severity === 'error',
    'bg-yellow-400': severity === 'warning',
    'bg-slate-400': severity === 'info'
})

onMounted(load)
</script>
