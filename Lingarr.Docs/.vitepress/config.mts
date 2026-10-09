import { defineConfig } from 'vitepress'

export default defineConfig({
    vite: {
        server: {
            watch: {
                usePolling: true
            }
        }
    },
    title: 'Lingarr Next',
    description: 'Documentation for Lingarr Next, the subtitle translation service.',
    themeConfig: {
        outline: { level: [2, 3] },
        nav: [
            { text: 'Home', link: '/' },
            { text: 'Getting Started', link: '/getting-started/installation' },
            { text: 'Developer API', link: '/developers/Plugins' }
        ],
        sidebar: [
            {
                text: 'Getting Started',
                items: [
                    { text: 'Installation', link: '/getting-started/installation' },
                    { text: 'Configuration', link: '/getting-started/configuration' }
                ]
            },
            {
                text: 'Translation Services',
                items: [
                    { text: 'Overview', link: '/translation-services/' },
                    { text: 'AI Services', link: '/translation-services/ai-services' },
                    { text: 'Machine Translation', link: '/translation-services/machine-translation' }
                ]
            },
            {
                text: 'Developers',
                items: [{ text: 'Developer API', link: '/developers/Plugins' }]
            }
        ],
        socialLinks: [{ icon: 'github', link: 'https://github.com/lingarr-translate/lingarr' }],
        search: {
            provider: 'local'
        }
    }
})
