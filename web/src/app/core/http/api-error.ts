import { HttpErrorResponse } from '@angular/common/http';

export function apiError(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const detail = (err.error as { detail?: string } | null)?.detail;
    if (detail) return detail;
    if (err.status === 0) return 'Cannot reach the API. Is it running on port 5080?';
    if (err.status === 401) return 'Please sign in again.';
    if (err.status === 403) return 'You do not have access to this action.';
    return err.message;
  }
  return 'Something went wrong.';
}
