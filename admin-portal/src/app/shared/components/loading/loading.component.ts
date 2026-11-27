import { Component, Input } from '@angular/core';
import { LoaderComponent } from '../loader/loader.component';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-loading',
  standalone: true,
  imports: [CommonModule, LoaderComponent],
  template: `<app-loader [message]="message"></app-loader>`,
  styles: [`:host{display:flex;flex-direction:column;flex:1}`],
})
export class LoadingComponent {
  @Input() message?: string;
  @Input() fullscreen = false;
}
