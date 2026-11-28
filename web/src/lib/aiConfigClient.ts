import { apiFetch } from './authClient';
import type {
  AiConfig,
  AiModel,
  AiProvider,
  WebSearchProviderKind,
} from '../types/atrium';

export async function getAiConfig(): Promise<AiConfig> {
  return apiFetch<AiConfig>('/api/ai/config', { method: 'GET' });
}

export async function createAiProvider(input: {
  name: string;
  baseUrl: string;
  apiKey?: string | null;
}): Promise<AiProvider> {
  return apiFetch<AiProvider>('/api/ai/providers', {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export async function updateAiProvider(
  id: string,
  input: {
    name: string;
    baseUrl: string;
    apiKey?: string | null;
    clearApiKey?: boolean;
  },
): Promise<AiProvider> {
  return apiFetch<AiProvider>(`/api/ai/providers/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

export async function deleteAiProvider(id: string): Promise<void> {
  await apiFetch<void>(`/api/ai/providers/${id}`, { method: 'DELETE' });
}

export async function discoverProviderModels(
  providerId: string,
): Promise<{ models: string[]; error?: string | null }> {
  return apiFetch<{ models: string[]; error?: string | null }>(
    `/api/ai/providers/${providerId}/discover-models`,
    { method: 'POST' },
  );
}

export async function createAiModel(input: {
  providerId: string;
  modelId: string;
  displayName?: string | null;
  supportsVision?: boolean;
  sortOrder?: number;
}): Promise<AiModel> {
  return apiFetch<AiModel>('/api/ai/models', {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export async function updateAiModel(
  id: string,
  input: {
    modelId: string;
    displayName?: string | null;
    supportsVision: boolean;
    sortOrder?: number;
  },
): Promise<AiModel> {
  return apiFetch<AiModel>(`/api/ai/models/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

export async function deleteAiModel(id: string): Promise<void> {
  await apiFetch<void>(`/api/ai/models/${id}`, { method: 'DELETE' });
}

export async function updateAiDefaults(input: {
  defaultModelId: string | null;
  defaultVisionModelId: string | null;
  webSearchProvider: WebSearchProviderKind;
  webSearchUrl?: string | null;
  webSearchApiKey?: string | null;
  clearWebSearchApiKey?: boolean;
}): Promise<AiConfig> {
  return apiFetch<AiConfig>('/api/ai/defaults', {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

