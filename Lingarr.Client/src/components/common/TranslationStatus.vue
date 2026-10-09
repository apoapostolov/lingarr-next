<template>
    <span :title="translationStatus.toString()">
        {{ displayStatus }}
    </span>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { TranslationStatus, TRANSLATION_STATUS } from '@/ts'

const props = defineProps<{
    translationStatus: TranslationStatus
    retryAttempt?: number | null
    retryMax?: number | null
}>()

const displayStatus = computed(() => {
    if (
        props.translationStatus === TRANSLATION_STATUS.CANCELLED
        && (props.retryAttempt ?? 0) > 0
        && (props.retryMax ?? 0) > 0
    ) {
        return `Cancelled (${props.retryAttempt}/${props.retryMax})`
    }

    switch (props.translationStatus) {
        case TRANSLATION_STATUS.INPROGRESS:
            return 'In Progress'
        default:
            return props.translationStatus
    }
})
</script>
