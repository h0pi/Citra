/** File types the API accepts (backend SupportedFileTypes). */
export const ACCEPTED_EXTENSIONS = ['.pdf', '.docx', '.txt', '.md'] as const;

/** For the file picker's `accept` attribute. */
export const ACCEPT_ATTRIBUTE = ACCEPTED_EXTENSIONS.join(',');

/** Same as the API's Storage:MaxUploadSizeMb. The API checks again; this only saves a pointless upload. */
export const MAX_UPLOAD_MB = 20;

const MAX_UPLOAD_BYTES = MAX_UPLOAD_MB * 1024 * 1024;

/**
 * Why a file cannot be uploaded (the same wording as the API), or null if it may be sent.
 * Checked before uploading, so the user does not wait for a 20 MB upload only to hear it was too big.
 */
export function uploadProblem(file: Pick<File, 'name' | 'size'>): string | null {
  const dot = file.name.lastIndexOf('.');
  const extension = dot >= 0 ? file.name.slice(dot).toLowerCase() : '';
  if (!(ACCEPTED_EXTENSIONS as readonly string[]).includes(extension)) {
    return `This file type is not supported. Upload ${ACCEPTED_EXTENSIONS.join(', ')}.`;
  }
  if (file.size === 0) {
    return 'The file is empty.';
  }
  if (file.size > MAX_UPLOAD_BYTES) {
    return `The file is larger than the ${MAX_UPLOAD_MB} MB limit.`;
  }
  return null;
}
