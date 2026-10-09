<template>
    <div class="space-y-5">
        <SaveNotification ref="saveNotification" />

        <div class="flex flex-wrap items-end gap-2">
            <label class="min-w-64 flex-1">
                <span class="mb-1 block text-sm font-semibold text-primary-content">
                    {{ title }} profile
                </span>
                <select
                    v-model.number="selectedId"
                    class="w-full rounded-md border border-accent bg-secondary px-3 py-2 text-primary-content"
                    @change="selectProfile">
                    <option v-for="profile in profiles" :key="profile.id" :value="profile.id">
                        {{ profile.name }}
                        {{ profile.currentVersionNumber ? `· v${profile.currentVersionNumber}` : '· draft' }}
                    </option>
                </select>
            </label>
            <ButtonComponent variant="secondary" size="sm" @click="startNew">
                New profile
            </ButtonComponent>
        </div>

        <div v-if="editing" class="space-y-4">
            <div class="flex flex-wrap items-center gap-2">
                <span
                    v-if="selected?.currentVersionNumber"
                    class="rounded-full border border-green-500/45 bg-green-500/10 px-2.5 py-1 text-xs font-semibold text-green-200">
                    Published v{{ selected.currentVersionNumber }}
                </span>
                <span
                    v-else
                    class="rounded-full border border-slate-500/45 bg-slate-500/10 px-2.5 py-1 text-xs font-semibold text-slate-300">
                    Draft only
                </span>
                <span
                    v-if="selected?.hasUnpublishedChanges || creating"
                    class="rounded-full border border-yellow-500/45 bg-yellow-500/10 px-2.5 py-1 text-xs font-semibold text-yellow-200">
                    Unpublished changes
                </span>
                <span
                    v-if="isActive"
                    class="rounded-full border border-accent bg-accent/20 px-2.5 py-1 text-xs font-semibold text-accent-content">
                    Current default
                </span>
            </div>

            <div class="grid gap-3 md:grid-cols-2">
                <label>
                    <span class="mb-1 block text-sm font-semibold text-primary-content">Name</span>
                    <input
                        v-model="name"
                        maxlength="120"
                        class="w-full rounded-md border border-accent bg-secondary px-3 py-2 text-primary-content"
                        placeholder="Profile name" />
                </label>
                <label>
                    <span class="mb-1 block text-sm font-semibold text-primary-content">
                        Description
                    </span>
                    <input
                        v-model="description"
                        class="w-full rounded-md border border-accent bg-secondary px-3 py-2 text-primary-content"
                        placeholder="Intended use" />
                </label>
            </div>

            <div
                class="rounded-md border border-accent/30 bg-primary/45 p-3 text-sm leading-6 text-primary-content/70">
                <p>{{ help }}</p>
                <p class="mt-2 text-xs text-yellow-200/80">
                    Do not include credentials or other secrets in prompt profiles.
                </p>
            </div>

            <div>
                <div class="mb-2 flex flex-wrap items-center justify-between gap-2">
                    <span class="text-sm font-semibold text-primary-content">Prompt content</span>
                    <ButtonComponent variant="ghost" size="xs" @click="loadExample">
                        Load recommended example
                    </ButtonComponent>
                </div>
                <TextAreaComponent
                    v-model="content"
                    :rows="18"
                    :min-height="320"
                    :placeholders="placeholders" />
                <p class="mt-2 text-xs text-primary-content/50">
                    {{ content.length.toLocaleString() }} / 40,000 characters. Longer prompts increase AI token usage.
                </p>
            </div>

            <label>
                <span class="mb-1 block text-sm font-semibold text-primary-content">
                    Version note
                </span>
                <input
                    v-model="changeNote"
                    class="w-full rounded-md border border-accent bg-secondary px-3 py-2 text-primary-content"
                    placeholder="Version summary" />
            </label>

            <div class="flex flex-wrap gap-2">
                <ButtonComponent
                    v-if="creating"
                    variant="secondary"
                    :disabled="!name.trim()"
                    :loading="saving"
                    @click="createDraft">
                    Create draft
                </ButtonComponent>
                <template v-else>
                    <ButtonComponent
                        variant="secondary"
                        :disabled="!name.trim()"
                        :loading="saving"
                        @click="saveDraft">
                        Save draft
                    </ButtonComponent>
                    <ButtonComponent
                        :disabled="!name.trim()"
                        :loading="publishing"
                        @click="publish">
                        Publish version
                    </ButtonComponent>
                    <ButtonComponent
                        variant="ghost"
                        :disabled="!selected?.currentPublishedVersionId || isActive"
                        @click="makeDefault">
                        Use as default
                    </ButtonComponent>
                    <ButtonComponent
                        variant="ghost"
                        :disabled="deleteBlocked"
                        :title="deleteBlockedReason"
                        @click="deleteProfile">
                        Delete profile
                    </ButtonComponent>
                </template>
                <ButtonComponent v-if="creating" variant="ghost" @click="cancelNew">
                    Cancel
                </ButtonComponent>
            </div>

            <details v-if="selected?.versions.length" class="rounded-md border border-accent/25 p-3">
                <summary class="cursor-pointer text-sm font-semibold text-primary-content">
                    Version history ({{ selected.versions.length }})
                </summary>
                <div class="mt-3 space-y-2">
                    <div
                        v-for="version in selected.versions"
                        :key="version.id"
                        class="flex flex-wrap items-center justify-between gap-2 rounded-md bg-primary/45 p-3">
                        <div>
                            <div class="text-sm font-semibold text-primary-content">
                                Version {{ version.versionNumber }}
                                <span
                                    v-if="version.id === selected.currentPublishedVersionId"
                                    class="ml-1 text-xs text-green-300">
                                    current
                                </span>
                            </div>
                            <div class="mt-0.5 text-xs text-primary-content/55">
                                {{ version.changeNote || 'No version note' }} ·
                                {{ formatDateTime(version.createdAt) }}
                            </div>
                        </div>
                        <ButtonComponent
                            variant="ghost"
                            size="xs"
                            @click="restore(version.id)">
                            Restore to draft
                        </ButtonComponent>
                    </div>
                </div>
            </details>
        </div>

        <div v-else class="py-6 text-center text-sm text-primary-content/55">
            No {{ title.toLowerCase() }} profiles exist yet.
            <button class="ml-1 text-accent underline" type="button" @click="startNew">
                Create one
            </button>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
    IPromptProfile,
    PromptProfileType,
    SETTINGS
} from '@/ts'
import services from '@/services'
import { formatDateTime } from '@/utils/date'
import ButtonComponent from '@/components/common/ButtonComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import TextAreaComponent from '@/components/common/TextAreaComponent.vue'

const props = defineProps<{
    type: PromptProfileType
    title: string
    help: string
    recommendedExample: string
}>()

const profiles = ref<IPromptProfile[]>([])
const selectedId = ref(0)
const activeId = ref(0)
const creating = ref(false)
const name = ref('')
const description = ref('')
const content = ref('')
const changeNote = ref('')
const saving = ref(false)
const publishing = ref(false)
const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)

const selected = computed(() => profiles.value.find((profile) => profile.id === selectedId.value))
const editing = computed(() => creating.value || !!selected.value)
const isActive = computed(() => selectedId.value === activeId.value)
const deleteBlockedReason = computed(() => {
    if (isActive.value) return 'Choose a different default profile before deleting this one.'
    if ((selected.value?.assignmentCount ?? 0) > 0)
        return 'Remove this profile from the translation service chain before deleting it.'
    return ''
})
const deleteBlocked = computed(() => !!deleteBlockedReason.value)
const activeSettingKey = computed(() =>
    props.type === 'system'
        ? SETTINGS.ACTIVE_SYSTEM_PROMPT_PROFILE_ID
        : SETTINGS.ACTIVE_CONTEXT_PROMPT_PROFILE_ID
)
const placeholders = computed(() => {
    const common = [
        {
            placeholder: '{sourceLanguage}',
            placeholderText: 'Insert {sourceLanguage}',
            title: 'Source language',
            description: 'Source subtitle language',
            required: props.type === 'system'
        },
        {
            placeholder: '{targetLanguage}',
            placeholderText: 'Insert {targetLanguage}',
            title: 'Target language',
            description: 'Target language',
            required: props.type === 'system'
        }
    ]
    if (props.type === 'system') return common
    return [
        ...common,
        {
            placeholder: '{lineToTranslate}',
            placeholderText: 'Insert {lineToTranslate}',
            title: 'Target subtitle line',
            description: 'Subtitle line to translate',
            required: true
        },
        {
            placeholder: '{contextBefore}',
            placeholderText: 'Insert {contextBefore}',
            title: 'Earlier context',
            description: 'Subtitle lines preceding the target',
            required: false
        },
        {
            placeholder: '{contextAfter}',
            placeholderText: 'Insert {contextAfter}',
            title: 'Later context',
            description: 'Subtitle lines following the target',
            required: false
        }
    ]
})

const fill = (profile: IPromptProfile) => {
    selectedId.value = profile.id
    name.value = profile.name
    description.value = profile.description
    content.value = profile.draftContent
    changeNote.value = ''
    creating.value = false
}

const refresh = async (selectId?: number) => {
    profiles.value = await services.promptProfile.list(props.type)
    const active = await services.setting.getSetting<string>(activeSettingKey.value)
    activeId.value = Number(active) || 0
    const profile =
        profiles.value.find((item) => item.id === selectId) ??
        profiles.value.find((item) => item.id === selectedId.value) ??
        profiles.value[0]
    if (profile) fill(profile)
}
const selectProfile = () => {
    const profile = selected.value
    if (profile) fill(profile)
}
const startNew = () => {
    creating.value = true
    selectedId.value = 0
    name.value = ''
    description.value = ''
    content.value = props.recommendedExample
    changeNote.value = ''
}
const cancelNew = async () => {
    creating.value = false
    await refresh()
}
const loadExample = () => {
    if (
        content.value.trim() &&
        !confirm('Replace the current draft with the recommended example?')
    )
        return
    content.value = props.recommendedExample
}
const createDraft = async () => {
    saving.value = true
    try {
        const profile = await services.promptProfile.create({
            type: props.type,
            name: name.value,
            description: description.value,
            content: content.value
        })
        await refresh(profile.id)
        saveNotification.value?.show()
    } finally {
        saving.value = false
    }
}
const saveDraft = async () => {
    if (!selected.value) return
    saving.value = true
    try {
        const profile = await services.promptProfile.saveDraft(selected.value.id, {
            name: name.value,
            description: description.value,
            content: content.value
        })
        await refresh(profile.id)
        saveNotification.value?.show()
    } finally {
        saving.value = false
    }
}
const publish = async () => {
    if (!selected.value) return
    if (!content.value.trim() && !confirm('Publish an empty prompt profile?')) return
    publishing.value = true
    try {
        await saveDraft()
        const profile = await services.promptProfile.publish(selected.value.id, changeNote.value)
        await refresh(profile.id)
        saveNotification.value?.show()
    } finally {
        publishing.value = false
    }
}
const makeDefault = async () => {
    if (!selected.value) return
    await services.promptProfile.activate(selected.value.id)
    await refresh(selected.value.id)
    saveNotification.value?.show()
}
const restore = async (versionId: number) => {
    if (!selected.value) return
    const profile = await services.promptProfile.restore(selected.value.id, versionId)
    await refresh(profile.id)
    saveNotification.value?.show()
}
const deleteProfile = async () => {
    if (!selected.value) return
    if (
        !confirm(
            `Delete “${selected.value.name}”? Profiles already used by translations will be archived to preserve history.`
        )
    )
        return
    await services.promptProfile.delete(selected.value.id)
    await refresh()
}

onMounted(refresh)
</script>
