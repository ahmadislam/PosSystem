import { Component, HostListener, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { apiError } from '../../core/http/api-error';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  username = 'admin';
  password = '';
  showPassword = false;
  readonly introVisible = signal(true);

  constructor() {
    window.setTimeout(() => this.introVisible.set(false), 1500);
  }

  @HostListener('document:pointermove', ['$event'])
  trackPointer(event: PointerEvent): void {
    const x = (event.clientX / Math.max(window.innerWidth, 1) - 0.5) * 2;
    const y = (event.clientY / Math.max(window.innerHeight, 1) - 0.5) * 2;
    document.documentElement.style.setProperty('--pointer-x', x.toFixed(3));
    document.documentElement.style.setProperty('--pointer-y', y.toFixed(3));
  }
  readonly busy = signal(false);
  readonly demoBusy = signal(false);
  readonly error = signal<string | null>(null);

  submit(): void {
    this.busy.set(true);
    this.error.set(null);
    this.auth.login(this.username.trim(), this.password).subscribe({
      next: () => {
        void this.router.navigate([this.auth.isStaff() ? '/dashboard' : '/pos']);
      },
      error: (err) => {
        this.error.set(apiError(err));
        this.busy.set(false);
      },
    });
  }

  exploreDemo(): void {
    this.demoBusy.set(true);
    this.error.set(null);
    this.auth.loginDemo().subscribe({
      next: () => void this.router.navigate(['/dashboard']),
      error: (err) => {
        this.error.set(apiError(err));
        this.demoBusy.set(false);
      },
    });
  }
}
