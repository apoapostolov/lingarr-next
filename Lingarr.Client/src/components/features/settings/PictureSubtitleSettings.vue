<template>
    <CardComponent title="Picture and caption subtitles">
        <template #description>
            Lingarr can turn English picture tracks and broadcast captions into a text subtitle,
            then queue that file for translation. Each tool and each format stays on until you turn
            it off. By default this waits, and other sources such as Bazarr are tried first.
        </template>
        <template #content>
            <SaveNotification ref="saveNotification" />
            <p class="rounded-md border border-accent/45 bg-secondary p-4 text-sm text-primary-content/90">
                <span class="font-semibold">Warning.</span>
                Scanning a large library is very intensive. The scan opens every movie and episode
                that has no English text subtitle and OCRs picture tracks one at a time. On a large
                library that can take many hours and use most of this container's memory.
            </p>

            <div class="grid gap-6 md:grid-cols-2">
                <section class="flex flex-col gap-3">
                    <div>
                        <span class="font-semibold">Subtitle Edit OCR</span>
                        <p class="text-sm text-secondary-content">
                            Reads picture subtitles with Tesseract.
                        </p>
                    </div>
                    <ToggleButton v-model="pictureOcr" aria-label="Subtitle Edit OCR">
                        <span class="text-sm font-medium text-primary-content">
                            {{ pictureOcr == 'true' ? 'Enabled' : 'Disabled' }}
                        </span>
                    </ToggleButton>
                    <ToggleButton
                        v-model="pgs"
                        :disabled="pictureOcr != 'true'"
                        aria-label="Blu-ray PGS">
                        <span class="text-sm text-primary-content">Blu-ray PGS</span>
                    </ToggleButton>
                    <ToggleButton
                        v-model="vobsub"
                        :disabled="pictureOcr != 'true'"
                        aria-label="DVD VobSub">
                        <span class="text-sm text-primary-content">DVD VobSub</span>
                    </ToggleButton>
                    <ToggleButton
                        v-model="dvb"
                        :disabled="pictureOcr != 'true'"
                        aria-label="DVB pictures">
                        <span class="text-sm text-primary-content">DVB pictures</span>
                    </ToggleButton>
                    <ToggleButton
                        v-model="xsub"
                        :disabled="pictureOcr != 'true'"
                        aria-label="XSUB">
                        <span class="text-sm text-primary-content">XSUB</span>
                    </ToggleButton>
                </section>

                <section class="flex flex-col gap-3">
                    <div>
                        <span class="font-semibold">CCExtractor</span>
                        <p class="text-sm text-secondary-content">
                            Reads CEA-608, CEA-708, and teletext. These are caption streams, not pictures.
                        </p>
                    </div>
                    <ToggleButton v-model="captionExtract" aria-label="CCExtractor">
                        <span class="text-sm font-medium text-primary-content">
                            {{ captionExtract == 'true' ? 'Enabled' : 'Disabled' }}
                        </span>
                    </ToggleButton>
                    <ToggleButton
                        v-model="eia608"
                        :disabled="captionExtract != 'true'"
                        aria-label="CEA-608">
                        <span class="text-sm text-primary-content">CEA-608</span>
                    </ToggleButton>
                    <ToggleButton
                        v-model="eia708"
                        :disabled="captionExtract != 'true'"
                        aria-label="CEA-708">
                        <span class="text-sm text-primary-content">CEA-708</span>
                    </ToggleButton>
                    <ToggleButton
                        v-model="teletext"
                        :disabled="captionExtract != 'true'"
                        aria-label="Teletext">
                        <span class="text-sm text-primary-content">Teletext</span>
                    </ToggleButton>
                </section>
            </div>

            <div class="flex flex-col gap-3">
                <div>
                    <span class="font-semibold">Use picture and caption tools last</span>
                    <p class="text-sm text-secondary-content">
                        On by default. Lingarr keeps a text subtitle from the video or from Bazarr
                        when it can. It converts a picture or caption track only after the wait, and
                        only if no text subtitle has appeared. Turn this off to convert as soon as
                        the file is found.
                    </p>
                </div>
                <ToggleButton v-model="lastResort" aria-label="Use picture and caption tools last">
                    <span class="text-sm font-medium text-primary-content">
                        {{ lastResort == 'true' ? 'Last resort' : 'Immediately' }}
                    </span>
                </ToggleButton>
                <div v-if="lastResort == 'true'" class="flex flex-col gap-2">
                    <div>
                        <span class="font-semibold">Wait after the file is found</span>
                        <p class="text-sm text-secondary-content">
                            Hours to wait. The default is 72. The clock starts when the file is added.
                            Conversion runs after that only if a text subtitle is still missing.
                        </p>
                    </div>
                    <InputComponent
                        v-model="waitHours"
                        :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                        label="Hours"
                        @update:validation="(val) => (waitHoursValid = val)" />
                </div>
            </div>

            <div class="flex flex-col gap-2">
                <button
                    class="w-fit cursor-pointer rounded border border-accent px-3 py-2 text-sm transition-colors hover:bg-accent hover:text-white disabled:cursor-not-allowed disabled:opacity-50"
                    type="button"
                    :disabled="!canScan || status == 'running'"
                    @click="scan">
                    {{ status == 'running' ? 'Scanning...' : 'Scan library' }}
                </button>
                <p class="text-sm text-secondary-content">
                    The scan converts matching files now and does not wait. It also runs while the
                    scheduled library scan is paused, writes an English subtitle, and queues it for
                    translation.
                </p>
                <p v-if="notice" class="text-sm text-secondary-content">{{ notice }}</p>
                <p v-if="summary" class="text-sm text-secondary-content">{{ summary }}</p>
            </div>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import axios from 'axios'
import { useSettingStore } from '@/store/setting'
import { INPUT_VALIDATION_TYPE, SETTINGS } from '@/ts'
import type { ISettings } from '@/ts'
import CardComponent from '@/components/common/CardComponent.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()
const notice = ref('')
const summary = ref('')
const status = ref('idle')
const waitHoursValid = ref(true)
let timer = 0

function bind(key: keyof ISettings) {
    return computed({
        get: (): string => (settingsStore.getSetting(key) as string) || 'true',
        set: (value: string): void => {
            settingsStore.updateSetting(key, value, true)
            saveNotification.value?.show()
        }
    })
}

const pictureOcr = bind(SETTINGS.PICTURE_OCR_ENABLED)
const captionExtract = bind(SETTINGS.CAPTION_EXTRACT_ENABLED)
const pgs = bind(SETTINGS.NONTEXT_PGS_ENABLED)
const vobsub = bind(SETTINGS.NONTEXT_VOBSUB_ENABLED)
const dvb = bind(SETTINGS.NONTEXT_DVB_ENABLED)
const xsub = bind(SETTINGS.NONTEXT_XSUB_ENABLED)
const eia608 = bind(SETTINGS.NONTEXT_EIA608_ENABLED)
const eia708 = bind(SETTINGS.NONTEXT_EIA708_ENABLED)
const teletext = bind(SETTINGS.NONTEXT_TELETEXT_ENABLED)
const lastResort = bind(SETTINGS.PICTURE_CONVERT_LAST_RESORT)

const waitHours = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.PICTURE_CONVERT_WAIT_HOURS) as string) || '72',
    set: (value: string): void => {
        settingsStore.updateSetting(SETTINGS.PICTURE_CONVERT_WAIT_HOURS, value, waitHoursValid.value)
        saveNotification.value?.show()
    }
})

const canScan = computed(() => {
    const pictures =
        pictureOcr.value == 'true' &&
        (pgs.value == 'true' || vobsub.value == 'true' || dvb.value == 'true' || xsub.value == 'true')
    const captions =
        captionExtract.value == 'true' &&
        (eia608.value == 'true' || eia708.value == 'true' || teletext.value == 'true')
    return pictures || captions
})

async function refresh() {
    const { data } = await axios.get<{ status: string; summary: string }>('/api/subtitle/picture-scan')
    status.value = data.status || 'idle'
    summary.value = data.summary || ''
    if (status.value == 'running' && timer == 0) {
        timer = window.setInterval(() => {
            refresh().catch(() => undefined)
        }, 5000)
    }
    if (status.value != 'running' && timer != 0) {
        window.clearInterval(timer)
        timer = 0
    }
}

async function scan() {
    notice.value = ''
    try {
        const { data } = await axios.post<{ started: boolean; message: string }>('/api/subtitle/picture-scan')
        notice.value = data.message
        await refresh()
    } catch {
        notice.value = 'Library scan failed to start.'
    }
}

onMounted(() => {
    refresh().catch(() => undefined)
})

onUnmounted(() => {
    if (timer != 0) {
        window.clearInterval(timer)
    }
})
</script>
