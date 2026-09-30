import { ACCEPT_ATTRIBUTE, MAX_UPLOAD_MB, uploadProblem } from './upload-rules';

const MB = 1024 * 1024;

describe('upload rules', () => {
  it('accepts the file types the API reads, in any letter case', () => {
    for (const name of ['a.pdf', 'b.docx', 'c.txt', 'd.md', 'HANDBOOK.PDF']) {
      expect(uploadProblem({ name, size: 100 })).toBeNull();
    }
    expect(ACCEPT_ATTRIBUTE).toBe('.pdf,.docx,.txt,.md');
  });

  it('refuses other types with the API’s wording', () => {
    for (const name of ['photo.jpg', 'old.doc', 'archive.pdf.zip', 'README']) {
      expect(uploadProblem({ name, size: 100 })).toBe(
        'This file type is not supported. Upload .pdf, .docx, .txt, .md.',
      );
    }
  });

  it('refuses empty files and files over the size limit', () => {
    expect(uploadProblem({ name: 'a.pdf', size: 0 })).toBe('The file is empty.');
    expect(uploadProblem({ name: 'a.pdf', size: MAX_UPLOAD_MB * MB })).toBeNull();
    expect(uploadProblem({ name: 'a.pdf', size: MAX_UPLOAD_MB * MB + 1 })).toBe(
      'The file is larger than the 20 MB limit.',
    );
  });
});
