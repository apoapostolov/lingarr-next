import {
    DirectoryItem,
    ILanguage,
    ISettings,
    ISubtitle,
    ITranslationRequest,
    MediaType,
    IPathMapping,
    IOnboardingRequest,
    ISignupRequest,
    ILoginRequest,
    IApiKeyResponse,
    IUser,
    IUpdateUserRequest,
    IIncludeSummary,
    IPluginManifest,
    IPluginOptionsResponse,
    IPluginStatus,
    IPluginSummary,
    IXaiOAuthDevice,
    IXaiOAuthPoll,
    IXaiOAuthStatus,
    IPlexPin,
    IPlexPoll,
    IPlexServer,
    IPlexStatus,
    IPlexTest,
    IProviderHealth,
    IProviderProbe,
    ITranslationQualityDetail,
    ITranslationQualitySummary,
    IDashboardActivity,
    ICreatePromptProfile,
    IPromptProfile,
    IPromptProfileDeleteResult,
    PromptProfileType,
    ISavePromptProfileDraft
} from '@/ts'

export interface Services {
    auth: IAuthService
    setting: ISettingService
    subtitle: ISubtitleService
    translate: ITranslateService
    translationRequest: ITranslationRequestService
    version: IVersionService
    media: IMediaService
    schedule: IScheduleService
    mapping: IMappingService
    directory: IDirectoryService
    statistics: IStatisticsService
    logs: ILogsService
    requestTemplate: IRequestTemplateService
    plugin: IPluginService
    xaiOAuth: IXaiOAuthService
    plex: IPlexService
    providerHealth: IProviderHealthService
    dashboard: IDashboardService
    promptProfile: IPromptProfileService
}

export interface IPromptProfileService {
    list(type?: PromptProfileType): Promise<IPromptProfile[]>
    create(request: ICreatePromptProfile): Promise<IPromptProfile>
    saveDraft(id: number, request: ISavePromptProfileDraft): Promise<IPromptProfile>
    publish(id: number, changeNote: string): Promise<IPromptProfile>
    restore(id: number, versionId: number): Promise<IPromptProfile>
    activate(id: number): Promise<void>
    delete(id: number): Promise<IPromptProfileDeleteResult>
}

export interface IDashboardService {
    activity(hours?: number): Promise<IDashboardActivity>
}

export interface IProviderHealthService {
    list(): Promise<IProviderHealth[]>
    test(provider: string): Promise<IProviderProbe>
}

export interface IPluginService {
    list(): Promise<IPluginSummary[]>
    getManifest(provider: string): Promise<IPluginManifest>
    getStatus(provider: string): Promise<IPluginStatus>
    getOptions(endpoint: string): Promise<IPluginOptionsResponse>
}

export interface IXaiOAuthService {
    status(): Promise<IXaiOAuthStatus>
    start(): Promise<IXaiOAuthDevice>
    poll(flowId: string): Promise<IXaiOAuthPoll>
    disconnect(): Promise<void>
}

export interface IPlexService {
    status(): Promise<IPlexStatus>
    startPin(): Promise<IPlexPin>
    pollPin(pinId: number): Promise<IPlexPoll>
    servers(): Promise<IPlexServer[]>
    selectServer(machineIdentifier: string, name: string, url: string): Promise<IPlexStatus>
    saveToken(url: string, token: string): Promise<IPlexStatus>
    test(): Promise<IPlexTest>
    logout(): Promise<void>
}

export interface IAuthService {
    completeOnboarding(request: IOnboardingRequest): Promise<void>
    authenticated(): Promise<void>
    signup(request: ISignupRequest): Promise<void>
    login(request: ILoginRequest): Promise<void>
    logout(): Promise<void>
    generateApiKey(): Promise<IApiKeyResponse>
    hasAnyUsers(): Promise<boolean>
    getUsers(): Promise<IUser[]>
    updateUser(id: number, request: IUpdateUserRequest): Promise<void>
    deleteUser(id: number): Promise<void>
}

export interface IMediaService {
    movies<T>(
        pageNumber: number,
        searchQuery: string,
        sortBy: string,
        ascending: boolean
    ): Promise<T>
    shows<T>(
        pageNumber: number,
        searchQuery: string,
        sortBy: string,
        ascending: boolean
    ): Promise<T>
    include<T>(mediaType: MediaType, id: number, include: boolean): Promise<T>
    includeAll<T>(mediaType: MediaType, include: boolean): Promise<T>
    includeSummary(mediaType: MediaType): Promise<IIncludeSummary>
    threshold<T>(mediaType: MediaType, id: number, hours: string): Promise<T>
}

export interface ISettingService {
    getSetting<T>(key: string): Promise<T>
    getSettings<T>(keys: string[]): Promise<T>
    setSetting(key: string, value: string): Promise<void>
    setSettings(keys: ISettings): Promise<void>
    getEncryptedSettings<T>(keys: string[]): Promise<T>
    setEncryptedSetting(key: string, value: string): Promise<void>
}

export interface ISubtitleService {
    collect<T>(path: string): Promise<T>
}

export interface IVersionService {
    getVersion<T>(): Promise<T>
}

export interface ITranslateService {
    translateSubtitle<T>(
        mediaId: number,
        subtitle: ISubtitle,
        source: string,
        target: ILanguage,
        mediaType: MediaType
    ): Promise<T>
    bulkTranslate<T>(mediaIds: number[], targetLanguage: string, mediaType: MediaType): Promise<T>
    getLanguages<T>(): Promise<T>
}

export interface ITranslationRequestService {
    get<T>(id: number): Promise<T>
    getActiveTranslations<T>(): Promise<T>
    requests<T>(
        pageNumber: number,
        searchQuery: string,
        sortBy: string,
        ascending: boolean
    ): Promise<T>
    cancel<T>(translationRequest: ITranslationRequest): Promise<T>
    remove<T>(translationRequest: ITranslationRequest): Promise<T>
    retry<T>(translationRequest: ITranslationRequest): Promise<T>
    resume<T>(translationRequest: ITranslationRequest): Promise<T>
    proofread<T>(translationRequest: ITranslationRequest): Promise<T>
    quality(id: number): Promise<ITranslationQualityDetail>
    reEvaluateQuality(id: number): Promise<ITranslationQualitySummary>
}

export interface IScheduleService {
    startJob<T>(jobName: string): Promise<T>
    recurringJobs<T>(): Promise<T>
    remove<T>(jobId: string): Promise<T>
    indexShows<T>(): Promise<T>
    indexMovies<T>(): Promise<T>
}

export interface IMappingService {
    getMappings(): Promise<IPathMapping[]>
    setMappings(mappings: IPathMapping[]): Promise<void>
}

export interface IDirectoryService {
    get(path: string): Promise<DirectoryItem[]>
}

export interface IStatisticsService {
    getStatistics<T>(): Promise<T>
    getDailyStatistics<T>(days?: number): Promise<T>
    resetStatistics(): Promise<void>
}

export interface ILogQuery {
    limit?: number
    before?: number
    after?: number
    minLevel?: string
}

export interface ILogsService {
    getPage<T>(query?: ILogQuery): Promise<T>
    clear(): Promise<void>
    getStream(after: number, minLevel?: string): EventSource
}

export interface IRequestTemplateService {
    getDefaults<T>(): Promise<T>
}
