import { Component, OnInit, NgZone, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { AccountService, CandidateDto } from '../../../services/account.service';
import { ToastService } from '../../../services/toast.service';
import { PopupService } from '../../../services/popup.service';

export interface TransferTargetJob {
  jobId: string;
  title: string;
  department: string;
}

@Component({
  selector: 'app-admin-candidate-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './candidate-management.html'
})
export class CandidateManagementComponent implements OnInit {
  private http = inject(HttpClient);
  private accountService = inject(AccountService);
  private toastService = inject(ToastService);
  private popup = inject(PopupService);
  private ngZone = inject(NgZone);
  private cdr = inject(ChangeDetectorRef);

  candidates: CandidateDto[] = [];
  isLoading = false;
  
  // Trạng thái nút Loading riêng cho từng id
  isToggling: { [key: string]: boolean } = {};

  // Pagination & Search
  searchTerm = '';
  pageConfig = 1;
  pageSize = 10;
  totalItems = 0;
  totalPages = 0;

  // Tab lọc nhanh chuyên sâu trong Kho nhân tài
  activeFilterTab: 'ALL' | 'TALENT_POOL' | 'HIGH_SCORE' | 'REHIRE' = 'ALL';

  // Modal Điều chuyển chiến dịch mới
  showTransferModal = false;
  transferCandidate: CandidateDto | null = null;
  transferAvailableJobs: TransferTargetJob[] = [];
  transferTargetJobId = '';
  transferReason = 'Điều phối từ Kho Nhân Tài (Talent Pool) sang đợt tuyển dụng mới';
  isTransferring = false;
  
  Math = Math; // Export Math object cho Template HTML dùng

  ngOnInit() {
    setTimeout(() => {
      this.loadCandidates();
    });
  }

  loadCandidates() {
    this.isLoading = true;
    this.cdr.detectChanges(); 
    
    this.accountService.getCandidates(this.searchTerm, this.pageConfig, this.pageSize)
      .subscribe({
        next: (res) => {
          this.candidates = res.data;
          this.totalItems = res.total;
          this.pageConfig = res.page;
          this.pageSize = res.pageSize;
          this.totalPages = Math.ceil(this.totalItems / this.pageSize) || 1;
          this.isLoading = false;
          this.cdr.detectChanges();
        },
        error: (err) => {
          this.toastService.error('Lỗi', 'Không thể tải danh sách kho nhân tài');
          this.isLoading = false;
          this.cdr.detectChanges();
        }
      });
  }

  get filteredCandidates(): CandidateDto[] {
    if (this.activeFilterTab === 'TALENT_POOL') {
      return this.candidates.filter(c => c.isInTalentPool);
    }
    if (this.activeFilterTab === 'HIGH_SCORE') {
      return this.candidates.filter(c => (c.aiMatchingScore || 0) >= 90);
    }
    if (this.activeFilterTab === 'REHIRE') {
      return this.candidates.filter(c => c.isExEmployeeRehire);
    }
    return this.candidates;
  }

  get stats() {
    return {
      all: this.totalItems,
      talentPool: this.candidates.filter(c => c.isInTalentPool).length,
      highScore: this.candidates.filter(c => (c.aiMatchingScore || 0) >= 90).length,
      rehire: this.candidates.filter(c => c.isExEmployeeRehire).length
    };
  }

  setFilterTab(tab: 'ALL' | 'TALENT_POOL' | 'HIGH_SCORE' | 'REHIRE') {
    this.activeFilterTab = tab;
  }

  changePage(newPage: number) {
    if (newPage >= 1 && newPage <= this.totalPages) {
      this.pageConfig = newPage;
      this.loadCandidates();
    }
  }

  getInitials(name: string): string {
    if (!name) return 'U';
    const parts = name.trim().split(' ');
    if (parts.length === 1) return parts[0].charAt(0).toUpperCase();
    return (parts[0].charAt(0) + parts[parts.length - 1].charAt(0)).toUpperCase();
  }

  viewCv(candidate: CandidateDto) {
    if (candidate.cvUrl) {
      window.open(candidate.cvUrl, '_blank');
    } else {
      this.toastService.info('Hồ sơ ứng viên', `Ứng viên ${candidate.fullName} nộp hồ sơ qua biểu mẫu tuyển dụng trực tiếp.`);
    }
  }

  openTransferModal(candidate: CandidateDto) {
    this.transferCandidate = candidate;
    this.transferReason = `Điều chuyển hồ sơ tiềm năng ${candidate.fullName} từ Kho Nhân Tài sang chiến dịch mới`;
    this.showTransferModal = true;

    this.http.get<any[]>('/api/jobs').subscribe({
      next: (jobs) => {
        const list = (jobs || [])
          .filter(j => j.status === 'OPEN' && j.jobId !== candidate.jobId)
          .map(j => ({
            jobId: j.jobId,
            title: j.title,
            department: j.departmentName || j.department || 'Phân xưởng'
          }));
        this.transferAvailableJobs = list;
        if (list.length > 0) {
          this.transferTargetJobId = list[0].jobId;
        }
        this.cdr.detectChanges();
      },
      error: () => {
        this.transferAvailableJobs = [];
      }
    });
  }

  closeTransferModal() {
    this.showTransferModal = false;
    this.transferCandidate = null;
    this.transferTargetJobId = '';
  }

  submitTransfer() {
    if (!this.transferCandidate || !this.transferTargetJobId || !this.transferReason.trim()) {
      this.toastService.error('Lỗi', 'Vui lòng chọn chiến dịch đích và nhập lý do điều chuyển.');
      return;
    }

    const appId = this.transferCandidate.applicationId;
    if (!appId) {
      this.toastService.error('Lỗi', 'Ứng viên chưa có mã hồ sơ gắn với chiến dịch.');
      return;
    }

    this.isTransferring = true;
    this.http.post<any>(`/api/applications/${appId}/transfer-job`, {
      targetJobId: this.transferTargetJobId,
      transferReason: this.transferReason.trim()
    }).subscribe({
      next: (res) => {
        this.isTransferring = false;
        this.toastService.success('Thành công', res.message || 'Đã điều chuyển hồ sơ sang chiến dịch mới.');
        this.closeTransferModal();
        this.loadCandidates();
      },
      error: (err) => {
        this.isTransferring = false;
        this.toastService.error('Thất bại', err.error?.message || 'Không thể điều chuyển hồ sơ.');
      }
    });
  }

  async toggleLock(candidate: CandidateDto) {
    const actionName = candidate.isActive ? 'Khóa' : 'Mở Khóa';
    let reason = '';
    
    if (candidate.isActive) {
      const p = await this.popup.prompt({
        title: 'Lý do khóa tài khoản',
        message: `Nhập lý do khóa tài khoản của ${candidate.fullName}`,
        placeholder: 'Ví dụ: Vi phạm quy định hồ sơ',
        confirmText: 'Tiếp tục',
        cancelText: 'Hủy',
        tone: 'neutral',
        multiline: true,
      });
      if (p === null) return;
      reason = p.trim();
    }

    const ok = await this.popup.confirm({
      title: `${actionName} ứng viên`,
      message: `Bạn có chắc muốn ${actionName.toLowerCase()} ứng viên này?`,
      confirmText: actionName,
      cancelText: 'Không',
      tone: candidate.isActive ? 'danger' : 'primary',
    });
    if (!ok) return;

    this.isToggling[candidate.userId] = true;

    this.accountService.toggleCandidateStatus(candidate.userId, reason)
      .subscribe({
        next: (res) => {
          this.toastService.success('Thành công', res.message);
          
          this.ngZone.run(() => {
            candidate.isActive = res.isActive;
            if (!res.isActive) {
              candidate.lockedByName = 'Bạn (Vừa xong)';
              candidate.lockedAt = new Date().toISOString();
              candidate.lockReason = reason || 'Bị khóa bởi quản trị viên';
            } else {
              candidate.lockedByName = undefined;
              candidate.lockedAt = undefined;
              candidate.lockReason = undefined;
            }

            this.isToggling[candidate.userId] = false;
            this.cdr.detectChanges();
          });
        },
        error: (err) => {
          setTimeout(() => {
            this.toastService.error('Thất bại', err.error?.message || 'Có lỗi xảy ra!');
            this.isToggling[candidate.userId] = false;
            this.cdr.detectChanges();
          });
        }
      });
  }
}
