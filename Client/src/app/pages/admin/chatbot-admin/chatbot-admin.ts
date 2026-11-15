import { Component, OnDestroy, OnInit, ChangeDetectorRef, NgZone } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NgFor, NgIf } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatbotAdminService, ChatbotFaqItem, GeminiKeyStat, ModuleInfo, UpsertChatbotFaqPayload } from '../../../services/chatbot-admin.service';
import { ToastService } from '../../../services/toast.service';
import { PopupService } from '../../../services/popup.service';

@Component({
    selector: 'app-chatbot-admin',
    standalone: true,
    imports: [CommonModule, FormsModule, NgIf, NgFor],
    templateUrl: './chatbot-admin.html',
    styleUrl: './chatbot-admin.scss'
})
export class ChatbotAdminComponent implements OnInit, OnDestroy {
    faqs: ChatbotFaqItem[] = [];
    filteredFaqs: ChatbotFaqItem[] = [];

    searchQuery = '';
    statusFilter: 'all' | 'active' | 'inactive' = 'all';

    isLoading = false;
    keyStats: GeminiKeyStat[] = [];
    isKeysLoading = false;
    isModalOpen = false;
    isSaving = false;

    // Quản lý Thêm Key
    isKeyModalOpen = false;
    newApiKey = '';
    isSavingKey = false;
    newKeyModules: { [key: string]: boolean } = {};

    // Quản lý Phân bổ module cho Key
    isAssignModalOpen = false;
    selectedKeyForAssign: GeminiKeyStat | null = null;
    selectedModulesMap: { [key: string]: boolean } = {};
    isSavingAssignment = false;

    supportedModules: ModuleInfo[] = [];

    modalMode: 'create' | 'edit' = 'create';
    editingFaqId: string | null = null;
    private keyRefreshHandle: ReturnType<typeof setInterval> | null = null;

    form: UpsertChatbotFaqPayload = {
        question: '',
        answer: '',
        category: 'Chung',
        keywords: '',
        priority: 0,
        isActive: true
    };

    constructor(
        private chatbotAdminService: ChatbotAdminService,
        private toast: ToastService,
        private popup: PopupService,
        private cdr: ChangeDetectorRef,
        private ngZone: NgZone
    ) { }

    ngOnInit(): void {
        this.loadFaqs();
        this.loadKeyStats();
        this.loadSupportedModules();
        this.startKeyAutoRefresh();
    }

    ngOnDestroy(): void {
        if (this.keyRefreshHandle) {
            clearInterval(this.keyRefreshHandle);
            this.keyRefreshHandle = null;
        }
    }

    loadSupportedModules(): void {
        this.chatbotAdminService.getSupportedModules().subscribe({
            next: (mods) => {
                this.supportedModules = mods || [];
                this.cdr.detectChanges();
            },
            error: () => {
                this.supportedModules = [
                    { id: 'Chatbot', name: 'Chatbot Tư Vấn & FAQ', description: 'Phản hồi câu hỏi ứng viên tự động' },
                    { id: 'JD', name: 'Sinh JD Tuyển Dụng AI', description: 'Tạo mô tả công việc, quyền lợi và yêu cầu' },
                    { id: 'Interview', name: 'Phỏng Vấn & Đánh Giá', description: 'Sinh câu hỏi phỏng vấn và chấm điểm' },
                    { id: 'CV', name: 'Sàng Lọc & Khớp CV', description: 'Bóc tách PDF CV và chấm điểm kỹ năng' },
                    { id: 'Email', name: 'Soạn Thảo Email Tuyển Dụng', description: 'Tạo thư mời phỏng vấn, thư trúng tuyển' },
                    { id: 'Code', name: 'Chấm Bài Test Code', description: 'Đánh giá bài test lập trình kỹ thuật' }
                ];
                this.cdr.detectChanges();
            }
        });
    }

    loadKeyStats(): void {
        this.isKeysLoading = true;
        this.chatbotAdminService.getGeminiKeys().subscribe({
            next: (items) => {
                this.ngZone.run(() => {
                    this.keyStats = items || [];
                    this.isKeysLoading = false;
                    setTimeout(() => {
                        this.cdr.detectChanges();
                    });
                });
            },
            error: (err) => {
                this.ngZone.run(() => {
                    this.isKeysLoading = false;
                    this.toast.error('Lỗi', err.error?.message || 'Không thể tải thống kê API keys.');
                    setTimeout(() => {
                        this.cdr.detectChanges();
                    });
                });
            }
        });
    }

    private startKeyAutoRefresh(): void {
        if (this.keyRefreshHandle) {
            clearInterval(this.keyRefreshHandle);
        }

        this.keyRefreshHandle = setInterval(() => {
            this.loadKeyStats();
        }, 15000);
    }

    loadFaqs(): void {
        this.isLoading = true;
        const status = this.statusFilter === 'all'
            ? undefined
            : this.statusFilter === 'active';

        this.chatbotAdminService.getFaqs(this.searchQuery, status).subscribe({
            next: (items) => {
                this.ngZone.run(() => {
                    this.faqs = items;
                    this.filteredFaqs = items;
                    this.isLoading = false;
                    setTimeout(() => {
                        this.cdr.detectChanges();
                    });
                });
            },
            error: (err) => {
                this.ngZone.run(() => {
                    this.isLoading = false;
                    this.toast.error('Lỗi', err.error?.message || 'Không thể tải dữ liệu FAQ chatbot.');
                    setTimeout(() => {
                        this.cdr.detectChanges();
                    });
                });
            }
        });
    }

    applyFilter(): void {
        this.loadFaqs();
    }

    openCreateModal(): void {
        this.modalMode = 'create';
        this.editingFaqId = null;
        this.form = {
            question: '',
            answer: '',
            category: 'Chung',
            keywords: '',
            priority: 0,
            isActive: true
        };
        this.isModalOpen = true;
    }

    openEditModal(item: ChatbotFaqItem): void {
        this.modalMode = 'edit';
        this.editingFaqId = item.faqId;
        this.form = {
            question: item.question,
            answer: item.answer,
            category: item.category,
            keywords: item.keywords || '',
            priority: item.priority,
            isActive: item.isActive
        };
        this.isModalOpen = true;
    }

    closeModal(): void {
        if (this.isSaving) return;
        this.isModalOpen = false;
        this.editingFaqId = null;
    }

    saveFaq(): void {
        if (!this.form.question.trim() || !this.form.answer.trim()) {
            this.toast.warning('Thiếu dữ liệu', 'Vui lòng nhập đầy đủ câu hỏi và câu trả lời.');
            return;
        }

        this.isSaving = true;
        const payload: UpsertChatbotFaqPayload = {
            question: this.form.question.trim(),
            answer: this.form.answer.trim(),
            category: this.form.category.trim() || 'Chung',
            keywords: this.form.keywords?.trim() || '',
            priority: Number(this.form.priority) || 0,
            isActive: this.form.isActive
        };

        const req$ = this.modalMode === 'create'
            ? this.chatbotAdminService.createFaq(payload)
            : this.chatbotAdminService.updateFaq(this.editingFaqId!, payload);

        req$.subscribe({
            next: (res) => {
                this.isSaving = false;
                this.toast.success('Thành công', res?.message || 'Đã lưu FAQ.');
                this.closeModal();
                this.loadFaqs();
            },
            error: (err) => {
                this.isSaving = false;
                this.toast.error('Lỗi', err.error?.message || 'Không thể lưu FAQ.');
            }
        });
    }

    toggleFaq(item: ChatbotFaqItem): void {
        this.chatbotAdminService.toggleFaq(item.faqId).subscribe({
            next: (res) => {
                item.isActive = res.isActive;
                this.toast.success('Thành công', res?.message || 'Đã cập nhật trạng thái.');
            },
            error: (err) => {
                this.toast.error('Lỗi', err.error?.message || 'Không thể đổi trạng thái FAQ.');
            }
        });
    }

    async deleteFaq(item: ChatbotFaqItem): Promise<void> {
        const confirmed = await this.popup.confirm({
            title: 'Xóa câu trả lời FAQ',
            message: `Bạn có chắc muốn xóa FAQ: "${item.question}"?`,
            confirmText: 'Xóa',
            cancelText: 'Hủy',
            tone: 'danger'
        });

        if (!confirmed) {
            return;
        }

        this.chatbotAdminService.deleteFaq(item.faqId).subscribe({
            next: (res) => {
                this.toast.success('Thành công', res?.message || 'Đã xóa FAQ.');
                this.loadFaqs();
            },
            error: (err) => {
                this.toast.error('Lỗi', err.error?.message || 'Không thể xóa FAQ.');
            }
        });
    }

    get keySummary() {
        return {
            total: this.keyStats.length,
            disabled: this.keyStats.filter(k => k.disabled).length,
            failures: this.keyStats.reduce((sum, k) => sum + (k.failureCount || 0), 0),
            success: this.keyStats.reduce((sum, k) => sum + (k.successCount || 0), 0)
        };
    }

    trackByKeyPreview(_: number, item: GeminiKeyStat): string {
        return item.keyPreview;
    }

    async disableKey(item: GeminiKeyStat): Promise<void> {
        const confirmed = await this.popup.confirm({
            title: 'Vô hiệu hóa key',
            message: `Bạn muốn tạm ngắt key ${item.keyPreview} khỏi vòng xoay sử dụng?`,
            confirmText: 'Vô hiệu hóa',
            cancelText: 'Hủy',
            tone: 'danger'
        });

        if (!confirmed) {
            return;
        }

        this.chatbotAdminService.disableGeminiKey(item.keyIndex).subscribe({
            next: (res) => {
                this.toast.success('Thành công', res?.message || 'Đã vô hiệu hóa key.');
                this.loadKeyStats();
            },
            error: (err) => {
                this.toast.error('Lỗi', err.error?.message || 'Không thể vô hiệu hóa key.');
            }
        });
    }

    async enableKey(item: GeminiKeyStat): Promise<void> {
        const confirmed = await this.popup.confirm({
            title: 'Kích hoạt key',
            message: `Bạn muốn bật lại key ${item.keyPreview} vào vòng xoay sử dụng?`,
            confirmText: 'Kích hoạt',
            cancelText: 'Hủy',
            tone: 'primary'
        });

        if (!confirmed) {
            return;
        }

        this.chatbotAdminService.enableGeminiKey(item.keyIndex).subscribe({
            next: (res) => {
                this.toast.success('Thành công', res?.message || 'Đã kích hoạt lại key.');
                this.loadKeyStats();
            },
            error: (err) => {
                this.toast.error('Lỗi', err.error?.message || 'Không thể kích hoạt key.');
            }
        });
    }

    async deleteKey(item: GeminiKeyStat): Promise<void> {
        const confirmed = await this.popup.confirm({
            title: 'Xóa vĩnh viễn Gemini API Key',
            message: `Bạn có chắc chắn muốn xóa key ${item.keyPreview}? Key này sẽ bị gỡ bỏ vĩnh viễn khỏi hệ thống và đồng bộ cập nhật vào appsettings.json.`,
            confirmText: 'Xóa key',
            cancelText: 'Hủy',
            tone: 'danger'
        });

        if (!confirmed) {
            return;
        }

        this.chatbotAdminService.deleteGeminiKey(item.keyIndex).subscribe({
            next: (res) => {
                this.toast.success('Thành công', res?.message || 'Đã xóa key thành công.');
                this.loadKeyStats();
            },
            error: (err) => {
                this.toast.error('Lỗi', err.error?.message || 'Không thể xóa key.');
            }
        });
    }

    openAssignModal(item: GeminiKeyStat): void {
        this.selectedKeyForAssign = item;
        this.selectedModulesMap = {};
        for (const m of this.supportedModules) {
            this.selectedModulesMap[m.id] = (item.assignedModules || []).includes(m.id);
        }
        this.isAssignModalOpen = true;
        this.cdr.detectChanges();
    }

    closeAssignModal(): void {
        if (this.isSavingAssignment) return;
        this.isAssignModalOpen = false;
        this.selectedKeyForAssign = null;
        this.cdr.detectChanges();
    }

    saveModuleAssignment(): void {
        if (!this.selectedKeyForAssign) return;
        this.isSavingAssignment = true;
        const selected = Object.keys(this.selectedModulesMap).filter(k => this.selectedModulesMap[k]);

        this.chatbotAdminService.assignModules(this.selectedKeyForAssign.keyIndex, selected).subscribe({
            next: (res) => {
                this.isSavingAssignment = false;
                this.isAssignModalOpen = false;
                this.selectedKeyForAssign = null;
                this.toast.success('Thành công', res?.message || 'Đã cập nhật phân bổ tính năng API.');
                this.loadKeyStats();
                this.cdr.detectChanges();
            },
            error: (err) => {
                this.isSavingAssignment = false;
                this.toast.error('Lỗi', err.error?.message || 'Không thể cập nhật phân bổ module.');
                this.cdr.detectChanges();
            }
        });
    }

    openAddKeyModal(): void {
        this.newApiKey = '';
        this.newKeyModules = {};
        // Mặc định chọn không tick hoặc tick gợi ý
        this.isKeyModalOpen = true;
        this.cdr.detectChanges();
    }

    closeAddKeyModal(): void {
        if (this.isSavingKey) return;
        this.newApiKey = '';
        this.newKeyModules = {};
        this.isKeyModalOpen = false;
        this.cdr.detectChanges();
    }

    saveNewGeminiKey(): void {
        const key = this.newApiKey.trim();
        if (!key) {
            this.toast.warning('Thiếu dữ liệu', 'Vui lòng nhập chuỗi Gemini API Key.');
            return;
        }

        this.isSavingKey = true;
        this.cdr.detectChanges();

        const selectedModules = Object.keys(this.newKeyModules).filter(k => this.newKeyModules[k]);

        this.chatbotAdminService.addGeminiKey(key, selectedModules).subscribe({
            next: (res) => {
                this.isSavingKey = false;
                this.isKeyModalOpen = false;
                this.newApiKey = '';
                this.newKeyModules = {};
                this.toast.success('Thành công', res?.message || 'Đã thêm Gemini API Key thành công và cập nhật appsettings.json.');
                this.loadKeyStats();
                this.cdr.detectChanges();
            },
            error: (err) => {
                this.isSavingKey = false;
                this.toast.error('Lỗi', err.error?.message || 'Không thể thêm Gemini API Key mới.');
                this.cdr.detectChanges();
            }
        });
    }
}
