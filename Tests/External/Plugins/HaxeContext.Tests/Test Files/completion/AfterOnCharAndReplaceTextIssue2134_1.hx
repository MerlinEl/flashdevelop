﻿package;
import flash.utils.Proxy;
class Issue2134_1 extends Proxy {
	override public function callProperty(name:Dynamic, ?p1:Dynamic, ?p2:Dynamic, ?p3:Dynamic, ?p4:Dynamic, ?p5:Dynamic):Dynamic {
		return super.callProperty(name, p1, p2, p3, p4, p5);
	}
}