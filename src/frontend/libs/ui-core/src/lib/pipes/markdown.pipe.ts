import { Pipe, PipeTransform, inject } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { marked } from 'marked';
import DOMPurify from 'dompurify';

@Pipe({
  name: 'markdown',
  standalone: true,
})
export class MarkdownPipe implements PipeTransform {
  private readonly sanitizer = inject(DomSanitizer);

  transform(value: string | null | undefined): SafeHtml {
    if (!value) return '';
    const html = marked.parse(value, { async: false }) as string;
    const clean = DOMPurify.sanitize(html, {
      ADD_TAGS: ['video', 'source', 'iframe'],
      ADD_ATTR: ['controls', 'autoplay', 'muted', 'loop', 'poster', 'allowfullscreen', 'frameborder'],
    });
    return this.sanitizer.bypassSecurityTrustHtml(clean);
  }
}
