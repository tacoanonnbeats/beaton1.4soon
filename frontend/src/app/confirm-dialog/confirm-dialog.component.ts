import { Component, OnInit, Injectable, Inject } from '@angular/core';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material';
import { AddEditPlaylistDialogComponent } from '../add-edit-playlist-dialog/add-edit-playlist-dialog.component';
import { AppIntegrationService } from '../services/app-integration.service';

@Component({
    selector: 'app-confirm-dialog',
    templateUrl: './confirm-dialog.component.html',
    styleUrls: ['./confirm-dialog.component.scss'],
})
@Injectable({
    providedIn: 'root',
})
export class ConfirmDialogComponent implements OnInit {
    constructor(
        public dialogRef: MatDialogRef<AddEditPlaylistDialogComponent>,
        @Inject(MAT_DIALOG_DATA) public data,
        private appIntegration: AppIntegrationService
    ) {}
    switched: boolean = false;
    ngOnInit() {
        this.dialogRef.afterOpened().subscribe(() => {
            console.log('should be calling hide browser');
            if (this.appIntegration.isAppLoaded()) {
                if (this.appIntegration.isBrowserShown) {
                    this.appIntegration.hideBrowser();
                    this.switched = true;
                }
            }
        });
        this.dialogRef.afterClosed().subscribe(() => {
            console.log('should be calling show browser');
            if (this.switched && this.appIntegration.isAppLoaded()) {
                this.appIntegration.showBrowser();
            }
        });
    }
    clickButton1() {
        this.dialogRef.close(1);
    }
    clickCancel() {
        this.dialogRef.close(-1);
    }
}
