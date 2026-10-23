import { Component, inject, signal } from '@angular/core';
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
  readonly busy = signal(false);
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
}
