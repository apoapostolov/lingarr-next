<template>
    <div class="flex items-center gap-1">
        <button
            type="button"
            class="text-primary-content hover:text-accent focus-visible:ring-accent rounded p-1 transition-colors focus-visible:ring-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-40"
            title="Download"
            aria-label="Download"
            :disabled="busy"
            @click.stop="download">
            <DownloadIcon class="h-4 w-4" :class="{ 'animate-pulse': downloadBusy }" />
        </button>
        <button
            type="button"
            class="text-primary-content hover:text-accent focus-visible:ring-accent rounded p-1 transition-colors focus-visible:ring-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-40"
            :title="hasSource ? 'Translate' : 'Translate (needs a source subtitle)'"
            :aria-label="hasSource ? 'Translate' : 'Translate (needs a source subtitle)'"
            :disabled="busy || !hasSource"
            @click.stop="run(false)">
            <LanguageIcon class="h-4 w-4" :class="{ 'animate-pulse': translateBusy }" />
        </button>
        <button
            type="button"
            class="text-primary-content hover:text-accent focus-visible:ring-accent rounded p-1 transition-colors focus-visible:ring-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-40"
            :title="hasSource ? 'Retranslate' : 'Retranslate (needs a source subtitle)'"
            :aria-label="hasSource ? 'Retranslate' : 'Retranslate (needs a source subtitle)'"
            :disabled="busy || !hasSource"
            @click.stop="run(true)">
            <RetryIcon class="h-4 w-4" :class="{ 'animate-pulse': overwriteBusy }" />
        </button>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { IForceTranslateResult, MediaType } from '@/ts'
import services from '@/services'
import DownloadIcon from '@/components/icons/DownloadIcon.vue'
import LanguageIcon from '@/components/icons/LanguageIcon.vue'
import RetryIcon from '@/components/icons/RetryIcon.vue'

const props = defineProps<{
    mediaId: number
    mediaType: MediaType
    hasSource: boolean
}>()

const emit = defineEmits<{
    done: [result: IForceTranslateResult]
}>()

const busy = ref(false)
const downloadBusy = ref(false)
const translateBusy = ref(false)
const overwriteBusy = ref(false)

async function download() {
    if (busy.value) return
    busy.value = true
    downloadBusy.value = true
    try {
        const result = await services.translate.download<IForceTranslateResult>(
            props.mediaId,
            props.mediaType
        )
        emit('done', result)
    } catch (error) {
        console.error('Subtitle download failed', error)
    } finally {
        busy.value = false
        downloadBusy.value = false
    }
}

async function run(overwrite: boolean) {
    if (busy.value || !props.hasSource) return
    busy.value = true
    overwriteBusy.value = overwrite
    translateBusy.value = !overwrite
    try {
        const result = await services.translate.force<IForceTranslateResult>(
            props.mediaId,
            props.mediaType,
            overwrite
        )
        emit('done', result)
    } catch (error) {
        console.error('Force translation failed', error)
    } finally {
        busy.value = false
        overwriteBusy.value = false
        translateBusy.value = false
    }
}
</script>
