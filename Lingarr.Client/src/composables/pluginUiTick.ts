import { ref } from 'vue'

export const pluginUiTick = ref(0)

export const bumpPluginUi = () => {
    pluginUiTick.value += 1
}
