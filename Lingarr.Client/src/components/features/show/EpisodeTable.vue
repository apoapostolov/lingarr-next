<template>
    <div class="w-full bg-tertiary text-tertiary-content">
        <div class="border-primary hidden grid-cols-12 border-b-2 font-bold md:grid">
            <div class="col-span-1 px-4 py-2">
                <span class="hidden lg:block">Episodes</span>
                <span class="block lg:hidden">#</span>
            </div>
            <div class="col-span-4 px-4 py-2">Title</div>
            <div class="col-span-4 px-4 py-2">Subtitles</div>
            <div class="col-span-1 flex items-center justify-center px-4 py-2">Include</div>
            <div class="col-span-2 flex items-center justify-center px-4 py-2">Translate</div>
        </div>
        <div
            v-for="episode in episodes"
            :key="episode.id"
            class="border-accent/20 hover:bg-accent/5 flex flex-wrap items-center gap-x-3 gap-y-2 border-b px-4 py-3 transition-colors md:grid md:grid-cols-12 md:gap-0 md:border-b-0 md:px-0 md:py-0">
            <div class="hidden px-4 py-2 md:col-span-1 md:block">
                {{ episode.episodeNumber }}
            </div>
            <div class="flex w-full items-center gap-2 md:col-span-4 md:w-auto md:px-4 md:py-2">
                <span class="text-primary-content/50 md:hidden">{{ episode.episodeNumber }}.</span>
                <span>{{ episode.title }}</span>
                <ActiveTranslationBadge :media-id="episode.id" :media-type="MEDIA_TYPE.EPISODE" />
            </div>
            <div
                class="flex w-full flex-wrap items-center gap-2 md:col-span-4 md:w-auto md:px-4 md:py-2">
                <div v-if="episode?.fileName" class="flex flex-wrap items-center gap-2">
                    <ContextMenu
                        v-for="(subtitle, jndex) in getSubtitle(episode.fileName)"
                        :key="`${episode.id}-${jndex}`"
                        :media-type="MEDIA_TYPE.EPISODE"
                        :media="episode"
                        :subtitle="subtitle">
                        <BadgeComponent>
                            {{ subtitle.language.toUpperCase() }}
                            <span v-if="subtitle.caption" class="text-primary-content/50">
                                - {{ subtitle.caption.toUpperCase() }}
                            </span>
                        </BadgeComponent>
                    </ContextMenu>
                </div>
            </div>
            <div
                class="flex items-center gap-2 md:col-span-1 md:justify-center md:px-4 md:py-2">
                <ToggleButton
                    v-model="episode.includeInTranslation"
                    size="small"
                    @toggle:update="
                        () =>
                            showStore.include(
                                MEDIA_TYPE.EPISODE,
                                episode.id,
                                episode.includeInTranslation
                            )
                    " />
                <span
                    class="text-primary-content/50 text-xs font-semibold tracking-wide uppercase md:hidden">
                    Include
                </span>
            </div>
            <div
                class="flex items-center md:col-span-2 md:justify-center md:px-4 md:py-2">
                <ForceTranslateControls
                    :media-id="episode.id"
                    :media-type="MEDIA_TYPE.EPISODE"
                    :has-source="hasSource(episode)"
                    @done="emit('translated')" />
            </div>
        </div>
    </div>
</template>
<script setup lang="ts">
import { IEpisode, ILanguage, ISubtitle, MEDIA_TYPE } from '@/ts'
import BadgeComponent from '@/components/common/BadgeComponent.vue'
import ContextMenu from '@/components/layout/ContextMenu.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import ActiveTranslationBadge from '@/components/common/ActiveTranslationBadge.vue'
import ForceTranslateControls from '@/components/common/ForceTranslateControls.vue'
import { useShowStore } from '@/store/show'
import { useSettingStore } from '@/store/setting'

const props = defineProps<{
    episodes: IEpisode[]
    subtitles: ISubtitle[]
}>()
const emit = defineEmits<{
    translated: []
}>()
const showStore = useShowStore()
const settingStore = useSettingStore()

const getSubtitle = (fileName: string | null) => {
    if (!fileName) return null
    return props.subtitles
        .filter(
            (subtitle: ISubtitle) =>
                subtitle.fileName.toLocaleLowerCase().includes(fileName.toLocaleLowerCase()) &&
                subtitle.language &&
                subtitle.language.trim() !== ''
        )
        .slice()
        .sort((a, b) => a.language.localeCompare(b.language))
}

const sourceCodes = (): string[] => {
    const raw = settingStore.getSettings.source_languages
    if (Array.isArray(raw)) {
        return raw.map((language: ILanguage) => language.code)
    }
    return []
}

const hasSource = (episode: IEpisode) => {
    const codes = sourceCodes()
    const found = getSubtitle(episode.fileName ?? null) ?? []
    return found.some((subtitle) => {
        const token = (subtitle.language || '').split(':')[0].trim().toLowerCase()
        return codes.some((code) => {
            const wanted = code.toLowerCase()
            return token === wanted || token.startsWith(wanted) || wanted.startsWith(token)
        })
    })
}
</script>
