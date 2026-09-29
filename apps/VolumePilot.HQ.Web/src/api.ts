export type Company = { id: string; name: string };
export type Session = {
  userId: string;
  email: string;
  activeCompanyId: string | null;
  companies: Company[];
};
export type Organization = {
  id: string;
  name: string;
  organizationType: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  revision: number;
};
export type Job = {
  id: string;
  clientOrganizationId: string;
  name: string;
  status: string;
  internalReference: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  revision: number;
};
export type Event = {
  id: string;
  jobId: string;
  name: string;
  startsAtUtc: string | null;
  endsAtUtc: string | null;
  timeZoneId: string | null;
  locationName: string | null;
  updatedAtUtc: string | null;
  revision: number;
};
export type Activity = {
  id: string;
  entityType: string;
  entityId: string;
  action: string;
  actorEmail: string;
  beforeJson: string;
  afterJson: string;
  occurredAtUtc: string;
};
export type Staff = {
  membershipId: string;
  email: string;
  role: string;
  joinedAtUtc: string;
};
export type Invitation = {
  id: string;
  email: string;
  role: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  acceptedAtUtc: string | null;
  revokedAtUtc: string | null;
};
export type LocalInvitation = {
  invitationId: string;
  companyName: string;
  email: string;
  token: string;
};

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}

async function parseResponse<T>(response: Response): Promise<T> {
  if (response.ok)
    return response.status === 204
      ? (undefined as T)
      : (response.json() as Promise<T>);
  const content = (await response.json().catch(() => null)) as {
    message?: string;
    detail?: string;
    title?: string;
    errors?: Record<string, string[]>;
  } | null;
  const validation =
    content?.errors && Object.values(content.errors).flat().join(" ");
  throw new ApiError(
    response.status,
    validation ||
      content?.message ||
      content?.detail ||
      (response.status === 401
        ? "Please sign in."
        : response.status === 403
          ? "You do not have access to this action."
          : response.status === 429
            ? "Too many attempts. Please wait a minute and try again."
            : content?.title || `Request failed (${response.status}).`),
  );
}

export async function get<T>(path: string): Promise<T> {
  return parseResponse<T>(await fetch(path, { credentials: "same-origin" }));
}

export async function post<T>(path: string, body: unknown): Promise<T> {
  return mutate<T>(path, 'POST', body);
}

export async function put<T>(path: string, body: unknown): Promise<T> {
  return mutate<T>(path, 'PUT', body);
}

async function mutate<T>(path: string, method: 'POST' | 'PUT', body: unknown): Promise<T> {
  const csrf = await get<{ requestToken: string }>("/api/auth/csrf");
  return parseResponse<T>(
    await fetch(path, {
      method,
      credentials: "same-origin",
      headers: {
        "Content-Type": "application/json",
        "X-CSRF-TOKEN": csrf.requestToken,
      },
      body: JSON.stringify(body),
    }),
  );
}
