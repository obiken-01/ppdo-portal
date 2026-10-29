// The six UAT personas from docs/v1.9/UAT_Polish_Plan.md §3.1.
// Credentials come from the environment, never the repo: E2E_<KEY>_EMAIL / E2E_<KEY>_PASSWORD.
// The login form's field is labelled "Username"; put whatever you type there in the *_EMAIL var.
// A persona with either variable missing is skipped, so you can start with one or two accounts.

export const PERSONA_KEYS = ["SA", "AD", "RV", "ST", "EN", "XD"] as const;
export type PersonaKey = (typeof PERSONA_KEYS)[number];

export interface Persona {
  key: PersonaKey;
  emailVar: string;
  passwordVar: string;
  email: string | undefined;
  password: string | undefined;
  /** Both variables are set. */
  configured: boolean;
  /** Relative to the frontend/ folder, where Playwright runs. Git-ignored. */
  storageState: string;
}

export const PERSONAS: Persona[] = PERSONA_KEYS.map((key) => {
  const emailVar = `E2E_${key}_EMAIL`;
  const passwordVar = `E2E_${key}_PASSWORD`;
  const email = process.env[emailVar] || undefined;
  const password = process.env[passwordVar] || undefined;
  return {
    key,
    emailVar,
    passwordVar,
    email,
    password,
    configured: !!email && !!password,
    storageState: `e2e/.auth/${key}.json`,
  };
});
