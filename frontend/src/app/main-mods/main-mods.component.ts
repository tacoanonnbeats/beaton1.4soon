import { AfterViewInit, Component, OnInit } from '@angular/core';
import { ConfigService } from '../services/config.service';
import { BeatOnApiService } from '../services/beat-on-api.service';
import { HostMessageService } from '../services/host-message.service';
import { QuestomConfig } from '../models/QuestomConfig';
import { BeatOnConfig } from '../models/BeatOnConfig';
import { ModCategory, ModDefinition, ModStatusType } from '../models/ModDefinition';
import { ClientSetModStatus } from '../models/ClientSetModStatus';
import { MatSlideToggleChange, MatDialog } from '@angular/material';
import { HostActionResponse } from '../models/HostActionResponse';
import { ECANCELED } from 'constants';
import { NgxSmartModalService } from 'ngx-smart-modal';
import { ConfirmDialogComponent } from '../confirm-dialog/confirm-dialog.component';
import { ClientDeleteMod } from '../models/ClientDeleteMod';
import { AppSettings } from '../appSettings';
import { BeatSaberColor } from '../models/BeatSaberColor';
import { ClientChangeColor, ColorType } from '../models/ClientChangeColor';

@Component({
    selector: 'app-main-mods',
    templateUrl: './main-mods.component.html',
    styleUrls: ['./main-mods.component.scss'],
})
export class MainModsComponent implements OnInit, AfterViewInit {
    config: QuestomConfig = <QuestomConfig>{ Mods: [] };
    beatSaberVersion: string = '';
    modSwitchInProgress: boolean = false;
    modIDBeingSwitched: string = null;
    selectedMod: ModDefinition;
    constructor(
        private configSvc: ConfigService,
        private beatOnApi: BeatOnApiService,
        private msgSvc: HostMessageService,
        public ngxSmartModalService: NgxSmartModalService,
        private dialog: MatDialog
    ) {
        this.configSvc.configUpdated.subscribe((cfg: BeatOnConfig) => {
            this.config = cfg.Config;
            this.beatSaberVersion = cfg.BeatSaberVersion;
        });
    }

    get leftColor() {
        if (this.config && this.config.LeftColor) {
            return (
                'rgba(' +
                this.config.LeftColor.R +
                ', ' +
                this.config.LeftColor.G +
                ', ' +
                this.config.LeftColor.B +
                ', ' +
                this.config.LeftColor.A +
                ')'
            );
        }
        return 'rgba(0,0,0,0)';
    }
    set leftColor(val) {}
    leftColorSelected(color) {
        console.log(color);
        color = color.substr(4, color.length - 5);
        var colors = color.split(',');
        var msg = new ClientChangeColor();
        msg.Color = <BeatSaberColor>{
            R: parseInt(colors[0]),
            G: parseInt(colors[1]),
            B: parseInt(colors[2]),
            A: colors.length > 3 ? Math.ceil(parseFloat(colors[3]) * 255) : 255,
        };
        msg.ColorType = ColorType.LeftColor;
        this.msgSvc.sendClientMessage(msg);
    }
    get rightColor() {
        if (this.config && this.config.RightColor) {
            return (
                'rgba(' +
                this.config.RightColor.R +
                ', ' +
                this.config.RightColor.G +
                ', ' +
                this.config.RightColor.B +
                ', ' +
                this.config.RightColor.A +
                ')'
            );
        }
        return 'rgba(0,0,0,0)';
    }
    set rightColor(val) {}
    rightColorSelected(color) {
        console.log(color);
        color = color.substr(4, color.length - 5);
        var colors = color.split(',');
        var msg = new ClientChangeColor();
        msg.Color = <BeatSaberColor>{
            R: parseInt(colors[0]),
            G: parseInt(colors[1]),
            B: parseInt(colors[2]),
            A: colors.length > 3 ? Math.ceil(parseFloat(colors[3]) * 255) : 255,
        };
        msg.ColorType = ColorType.RightColor;
        this.msgSvc.sendClientMessage(msg);
    }
    ngOnInit() {
        let isInit = false;
        this.configSvc.getConfig().subscribe((cfg: BeatOnConfig) => {
            this.config = cfg.Config;
            this.beatSaberVersion = cfg.BeatSaberVersion;
        });
    }
    getModBG(mod: ModDefinition) {
        if (!mod.CoverImageFilename) {
            if (mod.Category == ModCategory.Saber) {
                return 'url(../../assets/saber.png)';
            } else if (mod.Category == ModCategory.Note) {
                return 'url(../../assets/note.png)';
            } else if (mod.Category == ModCategory.Gameplay) {
                return 'url(../../assets/gameplay.png)';
            } else if (mod.Category == ModCategory.Library) {
                return 'url(../../assets/library.png)';
            } else {
                return 'url(../../assets/other.png)';
            }
        } else {
            let fixedUri = encodeURIComponent(mod.ID);
            fixedUri = fixedUri.replace('(', '%28').replace(')', '%29');
            return 'url(' + AppSettings.API_ENDPOINT + '/host/beatsaber/mod/cover?modid=' + fixedUri + ')';
        }
    }

    toggleMod(ev: MatSlideToggleChange, mod: ModDefinition) {
        const switchMod = () => {
            this.modIDBeingSwitched = mod.ID;
            this.modSwitchInProgress = true;
            let msg = new ClientSetModStatus();
            msg.ModID = mod.ID;
            msg.Status = ev.checked ? ModStatusType.Installed : ModStatusType.NotInstalled;
            let sub;
            sub = this.msgSvc.actionResponseMessage.subscribe((ev: HostActionResponse) => {
                if (ev.ResponseToMessageID == msg.MessageID) {
                    this.modIDBeingSwitched = null;
                    this.modSwitchInProgress = false;
                    sub.unsubscribe();
                    if (!ev.Success) {
                        //todo: show error
                        console.log('mod id ' + msg.ModID + ' did not switch properly');
                    }
                }
            });
            this.msgSvc.sendClientMessage(msg);
        };

        if (ev.checked && mod.TargetBeatSaberVersion != this.beatSaberVersion) {
            const dialogRef = this.dialog.open(ConfirmDialogComponent, {
                width: '470px',
                height: '240px',
                disableClose: true,
                data: {
                    title: 'Mod Compatibility Warning',
                    subTitle:
                        'The mod is for Beat Saber: ' +
                        mod.TargetBeatSaberVersion +
                        '\nYou have version:\t\t' +
                        this.beatSaberVersion +
                        '\nThis mod may fail to activate, it may cause Beat Saber to crash, or it may work fine.\nAre you sure you want to turn it on?',
                    button1Text: 'Enable Mod',
                },
            });
            dialogRef.afterClosed().subscribe(res => {
                if (res == 1) {
                    switchMod();
                } else {
                    ev.source.checked = false;
                }
            });
        } else {
            switchMod();
        }
    }
    getModSwitch(mod) {
        if (mod == null) return false;
        return !(
            (mod.Status != 'Installed' && mod.ID != this.modIDBeingSwitched) ||
            (mod.Status == 'Installed' && mod.ID == this.modIDBeingSwitched)
        );
    }

    onSelect(mod: ModDefinition): void {
        console.log(mod);
        this.selectedMod = mod;
    }
    clickDeleteMod(mod) {
        var dialogRef = this.dialog.open(ConfirmDialogComponent, {
            width: '450px',
            height: '180px',
            disableClose: true,
            data: { title: 'Delete ' + mod.Name + '?', subTitle: 'Are you sure you want to delete this mod?', button1Text: 'Yes' },
        });
        dialogRef.afterClosed().subscribe(res => {
            if (res == 1) {
                this.modSwitchInProgress = true;
                var msg = new ClientDeleteMod();
                msg.ModID = mod.ID;
                var sub;
                sub = this.msgSvc.actionResponseMessage.subscribe((ev: HostActionResponse) => {
                    if (ev.ResponseToMessageID == msg.MessageID) {
                        console.log('Got response message in mods for mod ID ' + msg.ModID);
                        this.modIDBeingSwitched = null;
                        this.modSwitchInProgress = false;
                        sub.unsubscribe();
                        if (!ev.Success) {
                            //todo: show error
                            console.log('mod id ' + msg.ModID + ' could not delete!');
                        }
                    }
                });
                console.log('Sending message to delete mod id ' + mod.ID);
                this.msgSvc.sendClientMessage(msg);
            }
        });
    }
    ngAfterViewInit() {}
}
